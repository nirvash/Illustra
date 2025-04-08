using Illustra.Helpers;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMagick;
using Illustra.Services;

namespace Illustra.ViewModels
{
    public enum PlayState
    {
        Playing,
        Paused,
        Stopped,
        Loading,
        Error
    }

    public class WebpPlayerViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IWebpAnimationService _animationService;
        private MagickImageCollection _collection;
        private DispatcherTimer _timer;
        private int _currentFrameIndex;
        private int _currentLoop;
        private int _totalLoops;
        private PlayState _currentState;
        private bool _isLoading;
        private bool _isFullScreen;
        private string _errorMessage;
        private BitmapSource _currentFrame;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string FilePath { get; private set; }

        public BitmapSource CurrentFrame
        {
            get => _currentFrame;
            private set { _currentFrame = value; OnPropertyChanged(nameof(CurrentFrame)); }
        }

        public int CurrentFrameIndex
        {
            get => _currentFrameIndex;
            set
            {
                if (_currentFrameIndex != value)
                {
                    Seek(value); // Sliderからの変更時にSeekを呼び出す
                }
            }
        }

        public int TotalFrames { get; private set; }

        public TimeSpan CurrentTime => CalculateCurrentTime();

        public TimeSpan TotalDuration { get; private set; }

        public int CurrentLoop
        {
            get => _currentLoop;
            private set { _currentLoop = value; OnPropertyChanged(nameof(CurrentLoop)); OnPropertyChanged(nameof(LoopCountText)); }
        }

        public int TotalLoops
        {
            get => _totalLoops;
            private set { _totalLoops = value; OnPropertyChanged(nameof(TotalLoops)); OnPropertyChanged(nameof(LoopCountText)); }
        }

        public string LoopCountText
        {
            get
            {
                if (TotalLoops == 0)
                    return $"(ループ中 / ∞)";
                else
                    return $"({_currentLoop} / {TotalLoops})";
            }
        }

        public PlayState CurrentState
        {
            get => _currentState;
            private set { _currentState = value; OnPropertyChanged(nameof(CurrentState)); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); }
        }

        public bool IsFullScreen
        {
            get => _isFullScreen;
            set { _isFullScreen = value; OnPropertyChanged(nameof(IsFullScreen)); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value; OnPropertyChanged(nameof(ErrorMessage)); }
        }

        public ICommand PlayCommand { get; private set; }
        public ICommand PauseCommand { get; private set; }
        public ICommand SeekCommand { get; private set; }
        public ICommand PreviousFrameCommand { get; private set; }
        public ICommand NextFrameCommand { get; private set; }
        public ICommand ToggleFullScreenCommand { get; private set; }

        public WebpPlayerViewModel(IWebpAnimationService animationService)
        {
            _animationService = animationService;
            _timer = new DispatcherTimer();
            _timer.Tick += Timer_Tick;

            PlayCommand = new RelayCommand(Play, CanPlay);
            PauseCommand = new RelayCommand(Pause, CanPause);
            PreviousFrameCommand = new RelayCommand(PreviousFrame, CanSeek);
            NextFrameCommand = new RelayCommand(NextFrame, CanSeek);
            ToggleFullScreenCommand = new RelayCommand(ToggleFullScreen);
        }

        public async Task LoadAsync(string filePath)
        {
            LogHelper.LogWithTimestamp("ViewModel.LoadAsync - Start", LogHelper.Categories.Performance);
            LogHelper.LogWithTimestamp("ViewModel.LoadAsync - IsLoading=true set", LogHelper.Categories.Performance);
            IsLoading = true; // ★最初に IsLoading を true にする
            CurrentState = PlayState.Loading;
            try
            {
                FilePath = filePath;

                LogHelper.LogWithTimestamp("ViewModel.LoadAsync - Before _animationService.LoadAsync", LogHelper.Categories.Performance);
                _collection = await _animationService.LoadAsync(filePath);
                TotalFrames = _animationService.GetTotalFrames(_collection); OnPropertyChanged(nameof(TotalFrames));
                LogHelper.LogWithTimestamp("ViewModel.LoadAsync - After _animationService.LoadAsync", LogHelper.Categories.Performance);
                TotalDuration = _animationService.GetTotalDuration(_collection); OnPropertyChanged(nameof(TotalDuration));
                TotalLoops = _animationService.GetLoopCount(_collection); OnPropertyChanged(nameof(TotalLoops)); // TotalLoopsも通知

                CurrentLoop = 1;
                CurrentFrameIndex = 0;
                UpdateFrame();

                CurrentState = PlayState.Stopped;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                CurrentState = PlayState.Error;
            }
            finally
            {
                IsLoading = false;
                LogHelper.LogWithTimestamp("ViewModel.LoadAsync - Finally", LogHelper.Categories.Performance);
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            AdvanceFrame();
        }

        private void AdvanceFrame()
        {
            if (_collection == null || TotalFrames == 0) return;

            _currentFrameIndex++; // フィールドを直接インクリメント
            OnPropertyChanged(nameof(CurrentFrameIndex)); // UIに通知
            OnPropertyChanged(nameof(CurrentTime)); // 時間も更新されたことを通知
            if (CurrentFrameIndex >= TotalFrames)
            {
                _currentFrameIndex = 0; // フィールドを直接リセット
                OnPropertyChanged(nameof(CurrentFrameIndex)); // UIに通知
                OnPropertyChanged(nameof(CurrentTime)); // 時間も更新されたことを通知
                if (TotalLoops != 0)
                {
                    CurrentLoop++;
                    if (CurrentLoop > TotalLoops)
                    {
                        Pause();
                        return;
                    }
                }
            }
            UpdateFrame();
        }

        private void UpdateFrame()
        {
            if (_collection == null) return;

            CurrentFrame = _animationService.GetFrameAsBitmapSource(_collection, CurrentFrameIndex);

            var delay = _animationService.GetFrameDelay(_collection, CurrentFrameIndex);
            _timer.Interval = delay;
        }

        public void Play()
        {
            if (_collection == null) return;

            CurrentState = PlayState.Playing;
            _timer.Start();
        }

        public void Pause()
        {
            _timer.Stop();
            CurrentState = PlayState.Paused;
        }

        // Sliderからの変更はプロパティセッター経由で処理されるため、
        // このメソッドは内部的なインデックス更新とフレーム更新に専念
        private void Seek(int frameIndex)
        {
            if (_collection == null || frameIndex < 0 || frameIndex >= TotalFrames) return;

            _currentFrameIndex = frameIndex; // 直接フィールドを更新
            OnPropertyChanged(nameof(CurrentFrameIndex)); // UIに通知
            OnPropertyChanged(nameof(CurrentTime)); // 時間も更新されたことを通知
            UpdateFrame();
        }

        public void PreviousFrame()
        {
            if (_collection == null) return;

            CurrentFrameIndex = (CurrentFrameIndex - 1 + TotalFrames) % TotalFrames;
            UpdateFrame();
        }

        public void NextFrame()
        {
            if (_collection == null) return;

            CurrentFrameIndex = (CurrentFrameIndex + 1) % TotalFrames;
            UpdateFrame();
        }

        public void ToggleFullScreen()
        {
            IsFullScreen = !IsFullScreen;
        }

        private bool CanPlay() => _collection != null && (CurrentState == PlayState.Paused || CurrentState == PlayState.Stopped);
        private bool CanPause() => _collection != null && CurrentState == PlayState.Playing;
        private bool CanSeek() => _collection != null;

        private TimeSpan CalculateCurrentTime()
        {
            if (_collection == null) return TimeSpan.Zero;

            double ms = 0;
            for (int i = 0; i < CurrentFrameIndex; i++)
            {
                ms += _animationService.GetFrameDelay(_collection, i).TotalMilliseconds;
            }
            return TimeSpan.FromMilliseconds(ms);
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer = null;
            _collection?.Dispose();
            _animationService?.Dispose();
        }
    }
}
