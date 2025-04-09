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
        private List<TimeSpan> _frameDelays;
        private DispatcherTimer _playbackTimer;
        private CancellationTokenSource _decodingCts; // LoadAsyncキャンセル用
        private CancellationTokenSource _prefetchCts; // 先読みタスクキャンセル用
        private int _currentFrameIndex;
        private AsyncManualResetEvent _frameAdvancedEvent = new AsyncManualResetEvent(false);
        private int _currentLoop;
        private int _totalLoops;
        private PlayState _currentState;
        private bool _isLoading;
        private bool _isFullScreen;
        private string _errorMessage;
        private BitmapSource _currentFrame;

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
                    Seek(value); // Sliderからの変更時にSeekを呼び出す
                }
            }
        }

        public int TotalFrames { get; private set; } // GetFeaturesAsyncで設定

        public TimeSpan CurrentTime => CalculateCurrentTime();

        public TimeSpan TotalDuration { get; private set; } // TODO: 正確な値を取得する方法が必要

        public int CurrentLoop
        {
            get => _currentLoop;
            private set { _currentLoop = value; OnPropertyChanged(nameof(CurrentLoop)); OnPropertyChanged(nameof(LoopCountText)); }
        }

        public int TotalLoops
        {
            get => _totalLoops;
            private set { _totalLoops = value; OnPropertyChanged(nameof(TotalLoops)); OnPropertyChanged(nameof(LoopCountText)); } // TODO: 正確な値を取得する方法が必要
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
        // public ICommand SeekCommand { get; private set; } // TODO: シーク実装後に有効化
        public ICommand PreviousFrameCommand { get; private set; }
        public ICommand NextFrameCommand { get; private set; }
        public ICommand ToggleFullScreenCommand { get; private set; }
        public WebpPlayerViewModel(IWebpAnimationService animationService)
        {
            _animationService = animationService ?? throw new ArgumentNullException(nameof(animationService));

            _frameCache = new ConcurrentDictionary<int, CachedFrame>(); // 型はOK
            _prefetchErrors = new ConcurrentDictionary<int, bool>(); // 初期化を追加
            _frameDelays = new List<TimeSpan>();

            _playbackTimer = new DispatcherTimer();
            _playbackTimer.Tick += PlaybackTimer_Tick;

            PlayCommand = new RelayCommand(Play, CanPlay);
            PauseCommand = new RelayCommand(Pause, CanPause);
            PreviousFrameCommand = new RelayCommand(PreviousFrame, CanSeek);
            NextFrameCommand = new RelayCommand(NextFrame, CanSeek);
            ToggleFullScreenCommand = new RelayCommand(ToggleFullScreen);
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
                _currentFrameIndex = (_currentFrameIndex + 1) % TotalFrames;
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
                    _currentFrameIndex = (_currentFrameIndex + 1) % TotalFrames;
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
            CurrentLoop = 1; // ループカウントもリセット

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
                    TotalLoops = 1;
                    OnPropertyChanged(nameof(TotalLoops));
                    CurrentLoop = 1;
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
                TotalLoops = 0;
                OnPropertyChanged(nameof(TotalLoops));
                CurrentLoop = 1;
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



        public void Play()
        {
            if (CurrentState == PlayState.Paused && _frameDelays.Count > 0)
            {
                CurrentState = PlayState.Playing;
                _playbackTimer.Start();
                LogHelper.LogWithTimestamp("Play: Playback resumed.", LogHelper.Categories.Performance);
                StartPrefetchLoop(); // 先読みループ開始
            }
        }

        public void Pause()
        {
            if (CurrentState == PlayState.Playing)
            {
                // filePath removed from DecodeFrameAtAsync call
                CurrentState = PlayState.Paused;
                _playbackTimer.Stop();
                LogHelper.LogWithTimestamp("Pause: Playback paused.", LogHelper.Categories.Performance);
                StopPrefetchLoop(); // 先読みループ停止
            }
        }

        // --- シーク関連 (libwebpストリーミングでは再実装が必要) ---
        private async void Seek(int frameIndex)
        {
            if (_frameDelays.Count == 0) return;

            if (frameIndex >= 0 && frameIndex < _frameDelays.Count)
            {
                var wasPlaying = CurrentState == PlayState.Playing;
                if (wasPlaying)
                {
                    _playbackTimer.Stop();
                }

                try
                {
                    // 既存のキャッシュを活用 (Demuxerはランダムアクセス可能)
                    // _prefetchErrors.Clear();
                    _currentFrameIndex = frameIndex;

                    // 目的のフレームの合成結果を取得（キャッシュ優先）
                    var targetComposedFrame = await GetComposedFrameAsync(frameIndex);

                    // 前のフレームが合成済みかチェック（エラー処理のため）
                    if (frameIndex > 0 && !_frameCache.ContainsKey(frameIndex - 1))
                    {
                        // 前のフレームが未合成の場合は事前に合成を試みる
                        await GetComposedFrameAsync(frameIndex - 1);
                    }
                    else
                    {
                        // 最初のフレームの場合は特に何もしない
                    }

                    // 目的のフレームの合成結果は targetComposedFrame に入っている
                    var composedFrame = targetComposedFrame;

                    // UIスレッドでシーク後のフレームを表示
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        CurrentFrame = composedFrame;
                        OnPropertyChanged(nameof(CurrentFrame));
                    });

                    // タイマーの間隔を設定
                    if (frameIndex >= 0 && frameIndex < _frameDelays.Count)
                    {
                        _playbackTimer.Interval = _frameDelays[frameIndex];
                    }
                    else
                    {
                        _playbackTimer.Interval = TimeSpan.FromMilliseconds(10);
                        LogHelper.LogWarning($"シーク: フレームインデックス {frameIndex} が無効です。デフォルトの間隔を使用します。");
                    }

                    if (wasPlaying)
                    {
                        _playbackTimer.Start();
                        LogHelper.LogWithTimestamp($"シーク: インデックス {frameIndex} で再生を再開しました。", LogHelper.Categories.Performance);
                        _frameAdvancedEvent.Set();
                        StartPrefetchLoop();
                    }
                    else
                    {
                        LogHelper.LogWithTimestamp($"シーク: 一時停止中にインデックス {frameIndex} に移動しました。", LogHelper.Categories.Performance);
                        _frameAdvancedEvent.Set();
                        StartPrefetchLoop();
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogError($"フレーム {frameIndex} へのシークに失敗しました: {ex.Message}", ex);
                    _prefetchErrors[frameIndex] = true; // シーク失敗をエラーとして記録
                }

                OnPropertyChanged(nameof(CurrentFrameIndex));
                OnPropertyChanged(nameof(CurrentTime));
            }
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

        private bool CanPlay() => CurrentState == PlayState.Paused; // 再生可能条件変更
        private bool CanPause() => CurrentState == PlayState.Playing; // 一時停止可能条件変更
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
                    WebpAnimationService.WebPDecodedFrame currentDecodedFrame;

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
