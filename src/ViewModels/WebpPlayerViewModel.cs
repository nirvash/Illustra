using System.Windows.Media;
using System.Windows;

using Illustra.Helpers;
using System.Collections.Concurrent;
using System;
using System.Threading;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
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
        private struct CachedFrame
        {
            public BitmapSource ComposedBitmap { get; set; } // 合成後のフレーム (Pbgra32)
            public int BaseFrameIndex { get; set; } // このフレームを合成する際に基準としたフレームIndex
        }
        private ConcurrentDictionary<int, CachedFrame> _frameCache;
        private ConcurrentDictionary<int, bool> _prefetchErrors;
        private bool _isSeekBarDragging;
        private bool _wasPlayingBeforeSeek; // シーク操作開始前の再生状態を保持
        private bool _isRepeatEnabled;
        private List<TimeSpan> _frameDelays;
        private DispatcherTimer _playbackTimer;
        private CancellationTokenSource _decodingCts; // LoadAsyncキャンセル用
        private CancellationTokenSource _prefetchCts; // 先読みタスクキャンセル用
        private int _currentFrameIndex;
        private AsyncManualResetEvent _frameAdvancedEvent = new AsyncManualResetEvent(false);
        private PlayState _currentState;
        private bool _isLoading;
        private bool _isFullScreen;
        private string _errorMessage;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string FilePath { get; private set; }

        public BitmapSource CurrentFrame { get; private set; } // 合成後のフレームを表示
        public int CurrentFrameIndex
        {
            get => _currentFrameIndex;
            set
            {
                if (_currentFrameIndex != value)
                {
                    Seek(value); // スライダーのドラッグ中でない場合のみSeekを呼び出す
                }
            }
        }

        public int TotalFrames { get; private set; } // GetFeaturesAsyncで設定

        public TimeSpan CurrentTime => CalculateCurrentTime();

        public TimeSpan TotalDuration { get; private set; } // TODO: 正確な値を取得する方法が必要

        // LoopCountText, CurrentLoop, TotalLoops は不要になったため削除

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

        public ICommand PlayPauseCommand { get; private set; }
        // public ICommand SeekCommand { get; private set; } // TODO: シーク実装後に有効化
        public ICommand PreviousFrameCommand { get; private set; }
        public ICommand NextFrameCommand { get; private set; }
        public ICommand ToggleFullScreenCommand { get; private set; }
        public ICommand ToggleRepeatCommand { get; private set; }
        public ICommand RewindCommand { get; private set; }
        public WebpPlayerViewModel(IWebpAnimationService animationService)
        {
            _animationService = animationService ?? throw new ArgumentNullException(nameof(animationService));

            _frameCache = new ConcurrentDictionary<int, CachedFrame>(); // 型はOK
            _prefetchErrors = new ConcurrentDictionary<int, bool>(); // 初期化を追加
            _frameDelays = new List<TimeSpan>();

            _playbackTimer = new DispatcherTimer();
            _playbackTimer.Tick += PlaybackTimer_Tick;

            PlayPauseCommand = new RelayCommand(PlayPause, () => true);
            // 設定からリピート状態を読み込む
            var settings = ViewerSettingsHelper.LoadSettings();
            IsRepeatEnabled = settings.VideoRepeatEnabled; // WebP用設定がないためVideo用を流用
            PreviousFrameCommand = new RelayCommand(PreviousFrame, CanSeek);
            NextFrameCommand = new RelayCommand(NextFrame, CanSeek);
            ToggleFullScreenCommand = new RelayCommand(ToggleFullScreen);
            ToggleRepeatCommand = new RelayCommand(ToggleRepeat);
            RewindCommand = new RelayCommand(Rewind, CanSeek);
        }

        private async void PlaybackTimer_Tick(object? sender, EventArgs e) // async void は UIイベントハンドラのため許容
        {
            if (_frameDelays.Count == 0 || CurrentState != PlayState.Playing) return;
            TimeSpan nextDelay;
            TimeSpan interval;

            var indexToDisplay = _currentFrameIndex;
            LogHelper.LogWithTimestamp($"PlaybackTick: Attempting to display frame {indexToDisplay}", LogHelper.Categories.Performance);

            // フレーム取得と表示
            try
            {
                // GetComposedFrameAsync がキャッシュ確認と必要に応じた再合成を行う
                var frameToDisplay = await GetComposedFrameAsync(indexToDisplay);


                if (frameToDisplay != null)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        CurrentFrame = frameToDisplay;
                        OnPropertyChanged(nameof(CurrentFrame));
                    });
                    LogHelper.LogWithTimestamp($"PlaybackTick: Displayed frame {indexToDisplay}", LogHelper.Categories.Performance);
                }
                else
                {
                    LogHelper.LogWarning($"PlaybackTick: Failed to get frame {indexToDisplay}");
                    return;
                }

                // 次のフレームへ
                int nextFrameIndex = (_currentFrameIndex + 1);
                if (nextFrameIndex >= TotalFrames)
                {
                    // 終端に達した場合
                    if (IsRepeatEnabled)
                    {
                        _currentFrameIndex = 0; // リピートONなら最初に戻る
                    }
                    else
                    {
                        // リピートOFFなら停止
                        PlayPause(); // Pause() -> PlayPause()
                        _currentFrameIndex = TotalFrames - 1; // 最終フレームに留まる
                        OnPropertyChanged(nameof(CurrentFrameIndex)); // UI更新
                        OnPropertyChanged(nameof(CurrentTime));
                        // シークバーの位置も最終フレームに設定する (UIスレッドで)
                        return; // タイマー更新は不要
                    }
                }
                else
                {
                    _currentFrameIndex = nextFrameIndex;
                }
                OnPropertyChanged(nameof(CurrentFrameIndex));
                OnPropertyChanged(nameof(CurrentTime));

                // 次のTickのインターバルを設定 (次のフレームの遅延時間)
                nextDelay = _frameDelays[_currentFrameIndex];
                interval = nextDelay > TimeSpan.Zero ? nextDelay : TimeSpan.FromMilliseconds(10);
                _playbackTimer.Interval = interval;
                LogHelper.LogWithTimestamp($"PlaybackTick: Set next interval for frame {_currentFrameIndex} to {interval.TotalMilliseconds}ms", LogHelper.Categories.Performance);
                _frameAdvancedEvent.Set(); // フレームが進んだことを通知
            }
            catch (Exception)
            {
                // プリフェッチエラーが発生したフレームか確認
                if (_prefetchErrors.ContainsKey(indexToDisplay))
                {
                    LogHelper.LogWarning($"PlaybackTick: Frame {indexToDisplay} failed to prefetch. Skipping.");
                    // エラーフレームはスキップして次のフレームへ
                    int nextErrorFrameIndex = (_currentFrameIndex + 1);
                    if (nextErrorFrameIndex >= TotalFrames)
                    {
                        if (IsRepeatEnabled)
                        {
                            _currentFrameIndex = 0;
                        }
                        else
                        {
                            PlayPause(); // Pause() -> PlayPause()
                            _currentFrameIndex = TotalFrames - 1;
                            OnPropertyChanged(nameof(CurrentFrameIndex)); // UI更新 (これによりSeekBarも更新されるはず)
                            OnPropertyChanged(nameof(CurrentTime));
                            return;
                        }
                    }
                    else
                    {
                        _currentFrameIndex = nextErrorFrameIndex;
                    }
                    OnPropertyChanged(nameof(CurrentFrameIndex));
                    OnPropertyChanged(nameof(CurrentTime));
                    // 次のTickのインターバルを設定 (次のフレームの遅延時間)
                    nextDelay = _frameDelays[_currentFrameIndex];
                    interval = nextDelay > TimeSpan.Zero ? nextDelay : TimeSpan.FromMilliseconds(10);
                    _playbackTimer.Interval = interval;
                    LogHelper.LogWithTimestamp($"PlaybackTick: Set next interval for frame {_currentFrameIndex} to {interval.TotalMilliseconds}ms after skipping error frame.", LogHelper.Categories.Performance);
                    _frameAdvancedEvent.Set(); // フレームが進んだことを通知
                }
                else
                {
                    // プリフェッチが単に遅れている可能性：短いインターバルでリトライ
                    LogHelper.LogWarning($"PlaybackTick: Frame {indexToDisplay} not found in cache, retrying shortly. Relying on prefetch.");
                    _playbackTimer.Interval = TimeSpan.FromMilliseconds(20);
                }
            }
        }

        public async Task LoadAsync(string filePath)
        {
            LogHelper.LogWithTimestamp("ViewModel.LoadAsync - Start", LogHelper.Categories.Performance);
            IsLoading = true;
            CurrentState = PlayState.Loading;
            ErrorMessage = null; // エラーメッセージをクリア

            // 既存のデコード処理があればキャンセル
            _decodingCts?.Cancel();
            _decodingCts?.Dispose();
            _decodingCts = new CancellationTokenSource();

            // エラー状態をリセット
            _frameCache.Clear();
            _prefetchErrors.Clear(); // エラー記録もクリア
            _frameDelays.Clear();
            _currentFrameIndex = -1;

            try
            {
                FilePath = filePath;
                LogHelper.LogWithTimestamp("ViewModel.LoadAsync - Before InitializeAsync", LogHelper.Categories.Performance);
                await _animationService.InitializeAsync(filePath); // Initialize service first
                LogHelper.LogWithTimestamp("ViewModel.LoadAsync - Before GetFeaturesAsync", LogHelper.Categories.Performance);
                var features = _animationService.GetFeatures(); // Use synchronous GetFeatures
                LogHelper.LogWithTimestamp($"ViewModel.LoadAsync - After GetFeaturesAsync: Width={features.width}, Height={features.height}, HasAnim={features.has_animation}", LogHelper.Categories.Performance);

                if (features.has_animation == 0)
                {
                    // 静止画の場合 (エラーまたは非アニメーション)
                    LogHelper.LogWarning($"File is not an animation or failed to get features: {filePath}");
                    TotalFrames = 1;
                    OnPropertyChanged(nameof(TotalFrames));
                    TotalDuration = TimeSpan.Zero;
                    OnPropertyChanged(nameof(TotalDuration));
                    _currentFrameIndex = 0;
                    OnPropertyChanged(nameof(CurrentFrameIndex));
                    OnPropertyChanged(nameof(CurrentTime));
                    CurrentState = PlayState.Stopped;
                    IsLoading = false;
                    LogHelper.LogWithTimestamp("ViewModel.LoadAsync - Non-animated or error", LogHelper.Categories.Performance);
                    return;
                }

                // アニメーションの場合
                TotalFrames = features.width > 0 && features.height > 0 ? 1 : 0;
                OnPropertyChanged(nameof(TotalFrames));
                TotalDuration = TimeSpan.Zero;
                OnPropertyChanged(nameof(TotalDuration));
                _currentFrameIndex = -1;
                LogHelper.LogWithTimestamp("ViewModel.LoadAsync - Before GetFrameDelaysAsync", LogHelper.Categories.Performance);
                LogHelper.LogWithTimestamp("ViewModel.LoadAsync - After DecodeAllFramesAsync call", LogHelper.Categories.Performance);

                // フレーム遅延情報を取得 (filePath removed)
                _frameDelays = _animationService.GetFrameDelays(); // Use synchronous GetFrameDelays
                TotalFrames = _frameDelays.Count;
                OnPropertyChanged(nameof(TotalFrames));

                // 最初のフレームをデコード
                try
                {
                    // 最初のフレームを合成して表示
                    var composedFirstFrame = await GetComposedFrameAsync(0);
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        CurrentFrame = composedFirstFrame;
                        OnPropertyChanged(nameof(CurrentFrame));
                    });
                    _currentFrameIndex = 0;
                    OnPropertyChanged(nameof(CurrentFrameIndex));
                    OnPropertyChanged(nameof(CurrentTime));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"フレームのデコード中にエラーが発生しました: {ex.Message}", ex);
                }

                // 総再生時間を計算
                TotalDuration = TimeSpan.FromMilliseconds(
                    _frameDelays.Sum(delay => delay.TotalMilliseconds)
                );
                OnPropertyChanged(nameof(TotalDuration));

                // 最初のフレームをデコード
                // 最初の数フレームを先読み
                // 最初のフレームは既にデコード済みなので、次のフレームから先読み
                // StartPrefetchLoop will handle prefetching from the current state
                // await PrefetchFramesAsync(1, Math.Min(5, TotalFrames - 1)); // Prefetching handled by loop now

                // 再生開始
                CurrentState = PlayState.Playing;
                if (_frameDelays.Count > 0)
                {
                    var interval = _frameDelays[0];
                    _playbackTimer.Interval = interval;
                    _playbackTimer.Start();
                    LogHelper.LogWithTimestamp($"LoadAsync: Playback started. First interval: {interval.TotalMilliseconds}ms", LogHelper.Categories.Performance);
                    _frameAdvancedEvent.Set(); // 最初のプリフェッチをトリガー
                    StartPrefetchLoop(); // 先読みループ開始
                }
                IsLoading = false; // ローディング完了
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"ViewModel.LoadAsync - Error", ex);
                ErrorMessage = ex.Message;
                CurrentState = PlayState.Error;
                IsLoading = false;
            }
        }

        public void PlayPause()
        {
            if (_frameDelays.Count == 0) return;

            if (CurrentState == PlayState.Playing)
            {
                // 再生中なら一時停止
                CurrentState = PlayState.Paused;
                _playbackTimer.Stop();
                LogHelper.LogWithTimestamp("Playback paused.", LogHelper.Categories.Performance);
                StopPrefetchLoop();
            }
            else if (CurrentState == PlayState.Paused)
            {
                // 一時停止中なら再生
                CurrentState = PlayState.Playing;
                _playbackTimer.Start();
                LogHelper.LogWithTimestamp("Playback resumed.", LogHelper.Categories.Performance);
                StartPrefetchLoop();
            }
        }

        // --- シーク関連 (libwebpストリーミングでは再実装が必要) ---
        private async void Seek(int frameIndex)
        {
            if (_frameDelays.Count == 0 || frameIndex < 0 || frameIndex >= _frameDelays.Count) return;

            // インデックスの更新はどの場合も行う
            _currentFrameIndex = frameIndex;
            OnPropertyChanged(nameof(CurrentFrameIndex));
            OnPropertyChanged(nameof(CurrentTime));

            try
            {
                await UpdateFrameDisplay(frameIndex);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"フレーム {frameIndex} へのシークに失敗しました: {ex.Message}", ex);
                _prefetchErrors[frameIndex] = true;
            }
        }

        private async Task UpdateFrameDisplay(int frameIndex)
        {
            BitmapSource? frame = null;
            if (_isSeekBarDragging)
            {
                frame = await GetFrameForSeekAsync(frameIndex);
            }
            else
            {
                frame = await GetComposedFrameAsync(frameIndex);
            }

            if (frame != null)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    CurrentFrame = frame;
                    OnPropertyChanged(nameof(CurrentFrame));
                });

                _frameAdvancedEvent.Set();
                StartPrefetchLoop();

                // タイマー制御は再生中の場合のみ
                if (CurrentState == PlayState.Playing)
                {
                    _playbackTimer.Stop();
                    _playbackTimer.Interval = _frameDelays[frameIndex];
                    _playbackTimer.Start();
                }
            }
        }

        /// <summary>
        /// シーク専用：キャッシュがあればそれを返し、なければ単体デコードして返す（キャッシュしない）
        /// </summary>
        private async Task<BitmapSource> GetFrameForSeekAsync(int frameIndex)
        {
            if (_frameCache.TryGetValue(frameIndex, out var cached))
            {
                return cached.ComposedBitmap;
            }
            return null;
        }

        public void PreviousFrame()
        {
            // TODO: ストリーミングデコーダーでのコマ戻し実装
            LogHelper.LogWarning("PreviousFrame function is not implemented for libwebp streaming decoder.");
            Seek(CurrentFrameIndex - 1); // 仮
        }
        public void NextFrame()
        {
            // TODO: ストリーミングデコーダーでのコマ送り実装
            LogHelper.LogWarning("NextFrame function is not implemented for libwebp streaming decoder.");
            Seek(CurrentFrameIndex + 1); // 仮
        }

        public void ToggleFullScreen()
        {
            // filePath removed from DecodeFrameAtAsync call
            IsFullScreen = !IsFullScreen;
        }

        public bool IsRepeatEnabled
        {
            get => _isRepeatEnabled;
            set
            {
                if (_isRepeatEnabled != value)
                {
                    _isRepeatEnabled = value;
                    OnPropertyChanged(nameof(IsRepeatEnabled));
                    // 設定を保存
                    var settings = ViewerSettingsHelper.LoadSettings();
                    settings.VideoRepeatEnabled = _isRepeatEnabled; // WebP用設定がないためVideo用を流用
                    ViewerSettingsHelper.SaveSettings(settings);
                }
            }
        }

        public void ToggleRepeat()
        {
            IsRepeatEnabled = !IsRepeatEnabled;
        }

        public void Rewind()
        {
            // 最初のフレームに戻る
            Seek(0);
        }

        private bool CanSeek() => CurrentState != PlayState.Loading && TotalFrames > 1; // TODO: シーク実装後に条件見直し

        private TimeSpan CalculateCurrentTime()
        {
            if (TotalFrames <= 0 || CurrentFrameIndex < 0 || _frameDelays.Count == 0)
                return TimeSpan.Zero;

            // 現在のフレームまでの累積時間を計算
            var totalMs = 0.0;
            for (var i = 0; i < CurrentFrameIndex && i < _frameDelays.Count; i++)
            {
                totalMs += _frameDelays[i].TotalMilliseconds;
            }
            return TimeSpan.FromMilliseconds(totalMs);
        }
        // PrefetchFramesAsync is obsolete

        private async Task PrefetchLoopAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            const int PrefetchCount = 5; // 先読みするフレーム数

            try
            {
                while (!token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        await _frameAdvancedEvent.WaitAsync(token);
                        _frameAdvancedEvent.Reset();

                        if (CurrentState != PlayState.Playing && CurrentState != PlayState.Paused)
                        {
                            continue;
                        }

                        // キャッシュに必要なフレーム数を計算
                        int neededInCache = 0;
                        int checkIndex = _currentFrameIndex;
                        int checkedCount = 0;

                        while (checkedCount < PrefetchCount && checkedCount < TotalFrames)
                        {
                            int targetIndex = (checkIndex + checkedCount) % TotalFrames;
                            if (!_frameCache.ContainsKey(targetIndex))
                            {
                                neededInCache++;
                            }
                            checkedCount++;
                        }

                        try
                        {
                            // フレームのプリフェッチ（キャッシュ優先）
                            for (int i = 1; i <= neededInCache && !token.IsCancellationRequested; i++)
                            {
                                token.ThrowIfCancellationRequested();
                                int targetIndex = (_currentFrameIndex + i) % TotalFrames;

                                // 既にキャッシュされているフレームはスキップ
                                if (_frameCache.ContainsKey(targetIndex))
                                {
                                    LogHelper.LogWithTimestamp(
                                        $"フレーム {targetIndex} は既にキャッシュされています。",
                                        LogHelper.Categories.Performance
                                    );
                                    continue;
                                }

                                // エラーが記録されているフレームはスキップ
                                if (_prefetchErrors.ContainsKey(targetIndex))
                                {
                                    continue;
                                }

                                try
                                {
                                    var composedFrame = await GetComposedFrameAsync(targetIndex);
                                    if (composedFrame != null)
                                    {
                                        LogHelper.LogWithTimestamp(
                                            $"フレーム {targetIndex} をプリフェッチ（合成完了）しました。",
                                            LogHelper.Categories.Performance
                                        );
                                    }
                                }
                                catch (OperationCanceledException) when (token.IsCancellationRequested)
                                {
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    _prefetchErrors[targetIndex] = true;
                                    LogHelper.LogError($"フレーム {targetIndex} のプリフェッチ中にエラー: {ex.Message}", ex);
                                }
                            }
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            LogHelper.LogError("プリフェッチ処理中に予期せぬエラーが発生しました。", ex);
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw; // 外側のキャッチブロックで処理
                    }
                    catch (Exception ex)
                    {
                        LogHelper.LogError("プリフェッチの待機中にエラーが発生しました。", ex);
                        continue; // エラーが発生しても次のイベントを待機
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                LogHelper.LogWithTimestamp("プリフェッチループがキャンセルされました。", LogHelper.Categories.Performance);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("プリフェッチループが予期せぬエラーで終了しました。", ex);
            }
            finally
            {
                _frameAdvancedEvent.Reset(); // 念のためリセット
                LogHelper.LogWithTimestamp("プリフェッチループを終了しました。", LogHelper.Categories.Performance);
            }
        }

        // フレーム合成とキャッシュ管理を行うメソッド
        private async Task<BitmapSource> GetComposedFrameAsync(int frameIndex)
        {
            BitmapSource result = null;

            try
            {
                // 1. キャッシュ確認（すぐには返さない）
                BitmapSource cachedBitmap = null;
                int currentCacheBaseIndex = int.MaxValue; // 初期値は最大値
                if (_frameCache.TryGetValue(frameIndex, out var currentCached))
                {
                    cachedBitmap = currentCached.ComposedBitmap;
                    currentCacheBaseIndex = currentCached.BaseFrameIndex;
                }

                // 2. キャッシュにない場合、合成に必要な情報を取得
                const int MaxComposeDepth = 10; // 最大何フレーム前まで遡って合成するか
                var framesToCompose = new List<WebpAnimationService.WebPDecodedFrame>();
                int baseFrameIndex = -1;
                BitmapSource baseCanvas = null; // 合成のベースとなるキャンバス (Pbgra32)

                // 1. MaxComposeDepth 前までのフレームのキャッシュをチェックし、
                // 最も深い（小さい）BaseFrameIndex を探す
                int requiredBaseIndex = int.MaxValue;
                for (int i = 0; i < MaxComposeDepth && frameIndex - i >= 0; i++)
                {
                    int checkIndex = frameIndex - i;
                    if (_frameCache.TryGetValue(checkIndex, out var cache))
                    {
                        requiredBaseIndex = Math.Min(requiredBaseIndex, cache.BaseFrameIndex);
                    }
                }
                if (requiredBaseIndex == int.MaxValue)
                {
                    requiredBaseIndex = Math.Max(0, frameIndex - MaxComposeDepth + 1); // キャッシュがない場合のフォールバック
                }
                LogHelper.LogWithTimestamp($"GetComposedFrameAsync({frameIndex}): MaxComposeDepth 内で最も深いベース {requiredBaseIndex} を目標とします。", LogHelper.Categories.Performance);

                for (int i = 0; i < MaxComposeDepth && frameIndex - i >= 0; i++)
                {
                    int currentIndex = frameIndex - i;

                    // まずキャッシュを確認
                    if (_frameCache.TryGetValue(currentIndex, out var existingCache))
                    {
                        // キャッシュヒット
                        // requiredBaseIndex はループ前に計算済み

                        // このキャッシュが十分な深度を持っているか確認
                        bool isSufficientDepth = existingCache.BaseFrameIndex <= requiredBaseIndex;

                        if (isSufficientDepth) // 十分な深度のキャッシュが見つかった場合
                        {
                            // 十分なキャッシュが見つかったので、これをベースとする
                            baseFrameIndex = existingCache.BaseFrameIndex; // キャッシュのBaseFrameIndexを継承
                            baseCanvas = existingCache.ComposedBitmap;
                            // framesToCompose には currentIndex+1 から frameIndex までの
                            // キャッシュになかったフレームが既に正しい順序で格納されているはず
                            LogHelper.LogWithTimestamp($"十分なキャッシュ {currentIndex} (Base:{baseFrameIndex}) を発見。これ以降の {framesToCompose.Count} フレームを合成します。", LogHelper.Categories.Performance);
                            break; // ベースが見つかったのでループ終了
                        }
                        else
                        {
                            // キャッシュの深度が不十分、または再合成が必要な場合：
                            // このキャッシュフレーム自体も再合成の対象とする必要があるため、
                            // デコードしてframesToComposeの先頭に追加し、探索を続ける。
                            LogHelper.LogWithTimestamp($"キャッシュ {currentIndex} は深度不足 (Base:{existingCache.BaseFrameIndex}, Required:{requiredBaseIndex})。デコードして探索継続。", LogHelper.Categories.Performance);

                            // キャッシュの深度が不十分なため、デコードして再合成対象に追加
                            var decoded = await _animationService.DecodeFrameAsync(currentIndex);
                            if (decoded == null) throw new InvalidOperationException($"フレーム {currentIndex} のデコードに失敗。");
                            framesToCompose.Insert(0, decoded); // リストの先頭に追加
                                                                // baseFrameIndex と baseCanvas は更新せず、さらに古いベースを探す
                        }
                    }
                    else
                    {
                        // キャッシュなし：デコードして情報を確認
                        var decoded = await _animationService.DecodeFrameAsync(currentIndex);
                        if (decoded == null) throw new InvalidOperationException($"フレーム {currentIndex} のデコードに失敗。");
                        framesToCompose.Insert(0, decoded); // リストの先頭に追加（古いフレームから処理するため）

                        // Dispose=1 (背景クリア) なら、これ以上遡る必要はない
                        if (decoded.DisposeMethod == LibWebP.WebPMuxAnimDispose.WEBP_MUX_DISPOSE_BACKGROUND)
                        {
                            baseFrameIndex = currentIndex;
                            baseCanvas = null; // 透明背景から開始
                            break; // ベースが見つかった（というかリセットされた）のでループ終了
                        }
                    }
                }

                // ベースが見つからなかった場合（MaxComposeDepthまで遡った or 最初のフレーム）
                if (baseFrameIndex == -1)
                {
                    // 最初のフレームから合成開始
                    baseFrameIndex = frameIndex - framesToCompose.Count + 1;
                    if (baseFrameIndex < 0) baseFrameIndex = 0; // 念のため
                    baseCanvas = null; // 透明背景から開始
                }

                // 3. フレーム合成
                BitmapSource currentCanvas = baseCanvas; // 初期キャンバス状態

                foreach (var frameToCompose in framesToCompose)
                {
                    // フレームを合成
                    var nextCanvas = ComposeSingleFrame(currentCanvas, frameToCompose); // ここでComposeSingleFrameを呼び出す
                    if (nextCanvas == null)
                    {
                        throw new InvalidOperationException($"フレーム {frameIndex} の合成に失敗しました。");
                    }
                    currentCanvas = nextCanvas;
                }

                // 4. 最終結果をキャッシュに格納
                if (currentCanvas != null)
                {
                    result = currentCanvas;
                    var newCachedFrame = new CachedFrame { ComposedBitmap = result, BaseFrameIndex = baseFrameIndex };
                    // キャッシュに格納 (ConcurrentDictionaryなので上書きはスレッドセーフ)
                    _frameCache[frameIndex] = newCachedFrame;
                    LogHelper.LogWithTimestamp($"フレーム {frameIndex} をキャッシュに追加/更新しました。", LogHelper.Categories.Performance);
                }
                else
                {
                    LogHelper.LogWarning($"フレーム {frameIndex} の合成に失敗しました。");
                }

                return result;
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"フレーム {frameIndex} の合成中にエラーが発生しました: {ex.Message}", ex);
                return null;
            }
        }

        // 1フレーム分の合成処理
        private BitmapSource ComposeSingleFrame(BitmapSource previousCanvas, WebpAnimationService.WebPDecodedFrame decodedFrame)
        {
            try
            {
                if (decodedFrame == null || decodedFrame.Bitmap == null)
                {
                    return null;
                }

                var frameBitmap = decodedFrame.Bitmap;
                var target = new RenderTargetBitmap(
                    _animationService.GetFeatures().width,
                    _animationService.GetFeatures().height,
                    96, 96, PixelFormats.Pbgra32);

                var drawingVisual = new DrawingVisual();
                using (var drawingContext = drawingVisual.RenderOpen())
                {
                    // 前のキャンバス状態を描画 (Dispose=1の場合は previousCanvas が null になっているはず)
                    if (previousCanvas != null) // BlendMethodに関わらず、前のキャンバスがあれば描画 (DrawImageがアルファ処理)
                    {
                        drawingContext.DrawImage(previousCanvas, new Rect(0, 0, target.Width, target.Height));
                    }
                    else
                    {
                        // 背景を透明でクリア (最初のフレーム、Dispose=1の後、またはBlend=0(上書き)の場合)
                        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, target.Width, target.Height));
                    }

                    // 現在のフレームを描画 (Bgra -> Pbgra 変換含む)
                    var convertedBitmap = ConvertBgraToPbgra(frameBitmap);
                    drawingContext.DrawImage(convertedBitmap,
                                        new Rect(decodedFrame.OffsetX, decodedFrame.OffsetY,
                                                 decodedFrame.Width, decodedFrame.Height));
                }

                target.Render(drawingVisual);
                target.Freeze();
                return target;
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"フレーム合成中にエラーが発生しました: {ex.Message}", ex);
                return null;
            }
        }

        private BitmapSource ConvertBgraToPbgra(BitmapSource bgraSource)
        {
            if (bgraSource.Format != PixelFormats.Bgra32)
            {
                // 必要に応じて他のフォーマットからの変換をサポート
                bgraSource = new FormatConvertedBitmap(bgraSource, PixelFormats.Bgra32, null, 0);
            }

            int width = bgraSource.PixelWidth;
            int height = bgraSource.PixelHeight;
            int stride = width * 4;
            byte[] pixelData = new byte[height * stride];
            bgraSource.CopyPixels(pixelData, stride, 0);

            // 事前乗算アルファに変換
            for (int i = 0; i < pixelData.Length; i += 4)
            {
                byte b = pixelData[i];
                byte g = pixelData[i + 1];
                byte r = pixelData[i + 2];
                byte a = pixelData[i + 3];

                if (a == 0)
                {
                    // アルファが0の場合、RGBも0にする
                    pixelData[i] = 0;
                    pixelData[i + 1] = 0;
                    pixelData[i + 2] = 0;
                }
                else if (a < 255)
                {
                    // アルファを乗算
                    float alphaFactor = a / 255.0f;
                    pixelData[i] = (byte)(b * alphaFactor);
                    pixelData[i + 1] = (byte)(g * alphaFactor);
                    pixelData[i + 2] = (byte)(r * alphaFactor);
                }
                // アルファが255の場合はRGBはそのまま
            }

            // 新しいBitmapSourceを作成
            var pbgraSource = BitmapSource.Create(width, height, 96, 96,
                                                 PixelFormats.Pbgra32, null,
                                                 pixelData, stride);
            pbgraSource.Freeze();
            return pbgraSource;
        }


        private void StartPrefetchLoop()
        {
            StopPrefetchLoop(); // 既存のループがあれば停止
            _prefetchCts = new CancellationTokenSource();
            Task.Run(() => PrefetchLoopAsync(_prefetchCts.Token));
            LogHelper.LogWithTimestamp("Prefetch loop started.", LogHelper.Categories.Performance);
        }

        private void StopPrefetchLoop()
        {
            _prefetchCts?.Cancel();
            _prefetchCts?.Dispose();
            _prefetchCts = null;
            LogHelper.LogWithTimestamp("Prefetch loop stopped.", LogHelper.Categories.Performance);
        }

        public void SeekBarStarted()
        {
            if (TotalFrames > 0 && !_isSeekBarDragging)
            {
                _isSeekBarDragging = true;
                _wasPlayingBeforeSeek = CurrentState == PlayState.Playing;
                if (_wasPlayingBeforeSeek)
                {
                    CurrentState = PlayState.Paused;
                    _playbackTimer.Stop();
                }
                _frameAdvancedEvent?.Set(); // プリフェッチサイクルのため
                StartPrefetchLoop();
            }
        }

        public async void SeekBarCompleted()
        {
            if (_isSeekBarDragging)
            {
                _isSeekBarDragging = false;

                // ドラッグ完了時に現在のフレームを表示
                await UpdateFrameDisplay(_currentFrameIndex);

                // 元の再生状態を復元
                if (_wasPlayingBeforeSeek)
                {
                    CurrentState = PlayState.Playing;
                    _playbackTimer.Start();
                }
            }
            _frameAdvancedEvent?.Set();
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }


        public void Dispose()
        {
            if (_playbackTimer != null)
            {
                _playbackTimer.Stop();
                _playbackTimer.Tick -= PlaybackTimer_Tick;
                _playbackTimer = null;
            }

            _frameCache?.Clear();
            _frameCache = null;

            _frameDelays?.Clear();
            _frameDelays = null;
            StopPrefetchLoop(); // 先読みループ停止

            _decodingCts?.Cancel();
            _decodingCts?.Dispose();
            _decodingCts = null;

            _animationService?.Dispose();
            // _frameAdvancedEvent は IDisposable ではないため、Dispose() は不要
        }
    }
}
