using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Illustra.Helpers; // LogHelperを使用するため
using System.Windows.Media;
using System.Windows.Controls.Primitives;

using System.Threading.Tasks;
using Illustra.ViewModels;


namespace Illustra.Controls
{
    public partial class VideoPlayerControl : UserControl
    {
        private DispatcherTimer _seekBarUpdateTimer;
        private DispatcherTimer? _hideControlsTimer;
        private bool _isSeekBarDragging = false;
        private bool _isVolumeSliderDragging = false;
        private string _currentFilePath = string.Empty;

        private bool _isStretchMode = false; // ストレッチモードフラグ

        private bool _wasPlayingBeforeSeek = false; // シーク操作開始前の再生状態

        private bool _isRepeatEnabled = false; // リピート再生フラグ
        public static readonly DependencyProperty FilePathProperty =
            DependencyProperty.Register("FilePath", typeof(string), typeof(VideoPlayerControl), new PropertyMetadata(null, OnFilePathChanged));

        public static readonly DependencyProperty IsFullScreenProperty =
            DependencyProperty.Register("IsFullScreen", typeof(bool), typeof(VideoPlayerControl), new PropertyMetadata(false, OnIsFullScreenChanged));

        public bool IsFullScreen
        {
            get { return (bool)GetValue(IsFullScreenProperty); }
            set { SetValue(IsFullScreenProperty, value); }
        }

        // Routed event for background double click
        public static readonly RoutedEvent BackgroundDoubleClickEvent =
            EventManager.RegisterRoutedEvent("BackgroundDoubleClick", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(VideoPlayerControl));

        // CLR event wrapper
        public event RoutedEventHandler BackgroundDoubleClick
        {
            add { AddHandler(BackgroundDoubleClickEvent, value); }
            remove { RemoveHandler(BackgroundDoubleClickEvent, value); }
        }

        private static void OnIsFullScreenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as VideoPlayerControl;
            control?.HandleFullScreenChanged((bool)e.NewValue);
        }


        private void HandleFullScreenChanged(bool isFullScreen)
        {
            if (isFullScreen)
            {
                // Start hidden in full screen, show on mouse move
                VideoControls.Visibility = Visibility.Collapsed;
                // Ensure timer is ready but not started
                InitializeHideControlsTimer();
                _hideControlsTimer?.Stop();
            }
            else
            {
                // Always visible when not full screen
                VideoControls.Visibility = Visibility.Visible;
                _hideControlsTimer?.Stop(); // Stop timer if running
            }
        }


        // Read-only dependency property to indicate if the mouse is over the controls
        private static readonly DependencyPropertyKey IsMouseOverControlsPropertyKey =
            DependencyProperty.RegisterReadOnly("IsMouseOverControls", typeof(bool), typeof(VideoPlayerControl), new PropertyMetadata(false));

        public static readonly DependencyProperty IsMouseOverControlsProperty =
            IsMouseOverControlsPropertyKey.DependencyProperty;

        public bool IsMouseOverControls
        {
            get { return (bool)GetValue(IsMouseOverControlsProperty); }
            private set { SetValue(IsMouseOverControlsPropertyKey, value); }
        }


        public string FilePath
        {
            get { return (string)GetValue(FilePathProperty); }
            set { SetValue(FilePathProperty, value); }
        }

        public VideoPlayerControl()
        {
            InitializeComponent();

            _seekBarUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _seekBarUpdateTimer.Tick += SeekBarUpdateTimer_Tick;
            ApplyInitialStretchMode(); // Apply initial stretch mode based on settings
        }


        private static void OnFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as VideoPlayerControl;
            if (control != null && e.NewValue is string newPath && !string.IsNullOrEmpty(newPath))
            {
                control.LoadVideo(newPath);
            }
            else
            {
                control?.StopVideo(); // パスがnullまたは空の場合は停止
            }
        }

        public void LoadVideo(string filePath)
        {
            if (_currentFilePath == filePath) return; // 同じファイルなら何もしない

            _currentFilePath = filePath;
            ApplyInitialStretchMode(); // Apply stretch mode when loading new video
            StopVideo(); // 以前の動画を停止

            try
            {
                VideoPlayer.Source = new Uri(filePath);
                // MediaOpenedイベントはXAMLで設定済み
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"動画の読み込み中にエラーが発生: {ex.Message}", ex);
                // エラー処理（例：メッセージ表示、デフォルト画像表示など）
                // ここではシンプルにログ出力のみ
            }
        }

        public void Play()
        {
            // 再生位置が終端に近い場合は先頭に戻す (再生終了後の再再生のため)
            if (VideoPlayer.NaturalDuration.HasTimeSpan &&
                Math.Abs(VideoPlayer.Position.TotalSeconds - VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds) < 0.1) // 誤差を考慮
            {
                VideoPlayer.Position = TimeSpan.Zero;
            }

            VideoPlayer.Play();
            _seekBarUpdateTimer.Start();
            PlayButton.Visibility = Visibility.Collapsed;
            PauseButton.Visibility = Visibility.Visible;
        }

        public void Pause()
        {
            VideoPlayer.Pause();
            _seekBarUpdateTimer.Stop();
            PlayButton.Visibility = Visibility.Visible;
            PauseButton.Visibility = Visibility.Collapsed;
        }

        // Stopボタンは「先頭に戻して一時停止」の動作に変更
        public void StopVideo()
        {
            VideoPlayer.Pause(); // Stop() から Pause() に変更
            VideoPlayer.Position = TimeSpan.Zero; // 再生位置を先頭に
            // VideoPlayer.Source = null; // Source はクリアしない
            _seekBarUpdateTimer.Stop();
            PlayButton.Visibility = Visibility.Visible; // 一時停止状態にする
            PauseButton.Visibility = Visibility.Collapsed;
            SeekBar.Value = 0;
            // TimeLabel を更新 (Position が 0 になったことを反映)
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                TimeLabel.Text = $"00:00 / {VideoPlayer.NaturalDuration.TimeSpan:mm\\:ss}";
            }
            else
            {
                TimeLabel.Text = "00:00 / 00:00";
            }
            // _currentFilePath = string.Empty; // パスはクリアしない
        }

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                SeekBar.Maximum = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                TimeLabel.Text = $"00:00 / {VideoPlayer.NaturalDuration.TimeSpan:mm\\:ss}";

                // Load volume from settings
                var settings = ViewerSettingsHelper.LoadSettings();
                VideoPlayer.Volume = settings.VideoVolume; // Apply saved volume
                VolumeSlider.Value = VideoPlayer.Volume; // Set slider value
                UpdateVolumeIcon(); // Update icon based on loaded volume

                ApplyInitialStretchMode(); // Apply stretch mode now that dimensions are known
                Play(); // MediaOpened後に再生開始
            }
        }

        private void RewindButton_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Position = TimeSpan.Zero;
            Pause(); // 先頭に戻して一時停止

            // UIを即時更新
            SeekBar.Value = 0;
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                TimeLabel.Text = $"00:00 / {VideoPlayer.NaturalDuration.TimeSpan:mm\\:ss}";
            }
            else
            {
                TimeLabel.Text = "00:00 / 00:00";
            }
        }


        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            Play();
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            Pause();
        } // PauseButton_Click を閉じる

        // RepeatButton_Click を StopButton_Click の後に移動 (StopButton_Click は削除)
        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleRepeat();
            // リピート状態に応じてボタンの背景色を変更
            // RepeatButton は XAML で x:Name が指定されていればアクセス可能
            RepeatButton.Background = _isRepeatEnabled ? Brushes.LightSkyBlue : Brushes.Transparent;
        }

        private void StretchModeButton_Click(object sender, RoutedEventArgs e)
        {
            _isStretchMode = !_isStretchMode; // Toggle stretch mode

            // Always update the UI state (icon/button visibility)
            UpdateStretchMode();
        }


        private void SeekBarUpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isSeekBarDragging && VideoPlayer.NaturalDuration.HasTimeSpan && VideoPlayer.Source != null)
            {
                SeekBar.Value = VideoPlayer.Position.TotalSeconds;
                TimeLabel.Text = $"{VideoPlayer.Position:mm\\:ss} / {VideoPlayer.NaturalDuration.TimeSpan:mm\\:ss}";
            }
        }

        private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) // async void -> void
        {
            // ドラッグ中はプレビュー位置を更新 (ScrubbingEnabled=True により Position 設定だけでOK)
            if (_isSeekBarDragging && VideoPlayer.Source != null && VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var newPosition = TimeSpan.FromSeconds(e.NewValue);
                VideoPlayer.Position = newPosition; // Position を設定 (ScrubbingEnabled によりプレビューが更新される)

                // async/Dispatcher/Play/Pause は不要

                // TimeLabel を更新
                TimeLabel.Text = $"{newPosition:mm\\:ss} / {VideoPlayer.NaturalDuration.TimeSpan:mm\\:ss}";
            }
        }



        // SeekBar_DragStarted は Thumb を直接ドラッグした場合に呼ばれる
        private void SeekBar_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            if (VideoPlayer.Source != null && SeekBar.IsEnabled) // IsEnabled チェックを追加
            {
                _isSeekBarDragging = true;
                _wasPlayingBeforeSeek = (PauseButton.Visibility == Visibility.Visible);
                _seekBarUpdateTimer.Stop();
                if (_wasPlayingBeforeSeek)
                {
                    VideoPlayer.Pause();
                }
            }
        }


        // 再生位置を確定し、シーク前の再生状態に戻す共通メソッド
        private void UpdatePositionAndState(double finalValue) // async を削除
        {
            if (VideoPlayer.Source != null)
            {
                VideoPlayer.Position = TimeSpan.FromSeconds(finalValue);
                _isSeekBarDragging = false; // ドラッグフラグをリセット

                // シーク前の状態に応じて再生/一時停止を決定
                if (_wasPlayingBeforeSeek)
                {
                    Play();
                }
                else
                {
                    // await Dispatcher.InvokeAsync(...) は不要
                    Pause(); // シーク前が一時停止中なら一時停止維持
                }
            }
            else
            {
                // ソースがない場合もフラグはリセット
                _isSeekBarDragging = false;
            }
        }

        // Thumb のドラッグ完了時に呼び出される
        private void SeekBar_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (_isSeekBarDragging) // ドラッグ中であった場合のみ処理
            {
                UpdatePositionAndState(SeekBar.Value);
            }
        }

        private void OnSeekBarMouseUp()
        {
            // Thumb のドラッグ完了は DragCompleted で処理されるため、ここではトラック部分のクリックのみを処理
            // ドラッグ中でなく、クリック位置が Thumb でない場合
            if (!_isSeekBarDragging)
            {

                // クリックされた位置に基づいて再生位置を更新
                // Slider の Value はクリック時に更新されているはず
                UpdatePositionAndState(SeekBar.Value);
            }
            // ドラッグ中の MouseUp は DragCompleted に任せるので何もしない
            // Thumb 上での MouseUp も DragCompleted に任せる
        }

        private void BackGuard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void BackGuard_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // ダブルクリックを無視 (何もしない)
            e.Handled = true;
        }

        private void SeekBarContainer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Thumb 自体の上でのクリックは DragStarted で処理されるため、ここでは無視
            if (e.OriginalSource is System.Windows.Controls.Primitives.Thumb)
            {
                return;
            }

            // Slider が有効で、ビデオがロードされている場合のみ処理
            if (sender is Border container && VideoPlayer.NaturalDuration.HasTimeSpan && container.ActualWidth > 0 && SeekBar.IsEnabled)
            {
                // ドラッグ状態を開始としてマーク
                _isSeekBarDragging = true;
                _wasPlayingBeforeSeek = (PauseButton.Visibility == Visibility.Visible); // シーク前の状態を保存
                _seekBarUpdateTimer.Stop(); // タイマー停止
                if (_wasPlayingBeforeSeek)
                {
                    VideoPlayer.Pause(); // 再生中なら一時停止
                }

                // Thumb を見つけてマウスキャプチャを設定し、ドラッグ操作を開始させる
                var thumb = FindVisualChild<Thumb>(SeekBar);
                if (thumb != null)
                {
                    // Thumb にマウスキャプチャを設定
                    // これにより、マウスボタンが押されている間、マウスイベントが Thumb に送られる
                    Dispatcher.InvokeAsync(() =>
                    {
                        thumb.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                        {
                            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                            Source = thumb
                        });
                        Mouse.Capture(thumb);
                    }, DispatcherPriority.Input);
                    // Slider の内部ロジックがこのキャプチャを検知し、ドラッグモードに入ることを期待
                }

                // イベントを処理済みにマーク (Slider 自身のデフォルト処理と競合しないように)
                // e.Handled = true; // 必要に応じてコメント解除
            }
        }

        private void SeekBarContainer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Thumb 自体の上でのリリースは DragCompleted で処理されるため、ここでは無視しない
            // トラッククリックからのドラッグ終了もここで処理する必要がある

            if (_isSeekBarDragging) // PreviewMouseLeftButtonDown で true に設定されているはず
            {
                // マウスキャプチャを解放 (PreviewMouseLeftButtonDown でキャプチャした場合)
                // Thumb がキャプチャしているか確認してから解放
                if (Mouse.Captured is Thumb capturedThumb && capturedThumb == FindVisualChild<Thumb>(SeekBar))
                {
                    Mouse.Capture(null);
                }

                // 現在の SeekBar.Value で再生位置を確定し、状態を復元
                // UpdatePositionAndState内で _isSeekBarDragging は false にリセットされる
                UpdatePositionAndState(SeekBar.Value);

                // イベントを処理済みにマーク (Slider 自身のデフォルト処理を抑制する場合)
                // e.Handled = true; // 必要に応じてコメント解除
            }
            // _isSeekBarDragging が false の場合は何もしない (予期せぬアップイベント)
        }

        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (_isRepeatEnabled)
            {
                VideoPlayer.Position = TimeSpan.Zero; // 先頭に戻す
                Play(); // 再度再生
            }
            else
            {
                Pause(); // 現在の位置で一時停止
                         // 再生終了時にシークバーを正確に終端に設定
                if (VideoPlayer.NaturalDuration.HasTimeSpan)
                {
                    SeekBar.Value = SeekBar.Maximum; // シークバーを最大値に設定
                                                     // TimeLabel も終端時刻を表示するように更新
                    TimeLabel.Text = $"{VideoPlayer.NaturalDuration.TimeSpan:mm\\:ss} / {VideoPlayer.NaturalDuration.TimeSpan:mm\\:ss}";
                }
            }
            // 必要に応じて再生終了イベントを外部に通知する
            // MediaEnded?.Invoke(this, EventArgs.Empty);
        }

        private void VolumeSliderContainer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ignore clicks directly on the Thumb (handled by Slider's built-in drag)
            if (e.OriginalSource is Thumb)
            {
                return;
            }

            // Handle clicks on the track part
            if (sender is Border && VolumeSlider.IsEnabled)
            {
                _isVolumeSliderDragging = true; // Mark as dragging (even for a click)

                // Find the Thumb and simulate a MouseLeftButtonDown event on it to initiate capture and drag
                var thumb = FindVisualChild<Thumb>(VolumeSlider);
                if (thumb != null)
                {
                    // Use Dispatcher to ensure the UI thread handles the event simulation
                    Dispatcher.InvokeAsync(() =>
                    {
                        thumb.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                        {
                            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                            Source = thumb
                        });
                        Mouse.Capture(thumb);
                    }, DispatcherPriority.Input);
                }
                // e.Handled = true; // Optionally mark handled if needed
            }
        }

        private void VolumeSliderContainer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isVolumeSliderDragging)
            {
                // Release mouse capture if it was captured by the Thumb
                if (Mouse.Captured is Thumb capturedThumb && capturedThumb == FindVisualChild<Thumb>(VolumeSlider))
                {
                    Mouse.Capture(null);
                }

                // Update the volume based on the final slider value
                // The ValueChanged event should have already updated the VideoPlayer.Volume and saved settings.
                // We just need to reset the dragging flag.
                _isVolumeSliderDragging = false;

                // e.Handled = true; // Optionally mark handled if needed
            }
        }

        public void ToggleRepeat()
        {
            _isRepeatEnabled = !_isRepeatEnabled;
            // TODO: XAML側でリピートボタンの表示状態を更新する (例: アイコン変更、背景色変更など)
            // 例: RepeatButton.Background = _isRepeatEnabled ? Brushes.LightBlue : Brushes.Transparent;
        }

        private void ApplyInitialStretchMode()
        {
            var settings = ViewerSettingsHelper.LoadSettings();
            _isStretchMode = !settings.FitSmallAnimationToScreen; // true if FitSmallAnimationToScreen is false

            // Update icon and button visibility based on the initial state
            UpdateStretchMode(); // Call without arguments
        }
        // Helper method to update stretch mode, icon, and button visibility based on current state and video size
        private void UpdateStretchMode()
        {
            if (!VideoPlayer.HasVideo || !VideoPlayer.NaturalDuration.HasTimeSpan || VideoPlayer.NaturalVideoWidth == 0 || VideoPlayer.NaturalVideoHeight == 0)
            {
                // Video not ready or size unknown, default to Uniform, hide button
                VideoPlayer.Stretch = Stretch.Uniform; // Ensure Uniform if not ready
                StretchModeIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialDesignKind.ImageAspectRatio;
                StretchModeButton.Visibility = Visibility.Collapsed;
                return;
            }

            var availableSize = GetAvailableVideoSize();
            bool canDisplayNone = VideoPlayer.NaturalVideoWidth <= availableSize.Width &&
                                  VideoPlayer.NaturalVideoHeight <= availableSize.Height;
            bool isStretch = canDisplayNone ? _isStretchMode : true; // Stretch mode only if it fits

            // Video fits, show the button and set icon based on current Stretch mode
            // IMPORTANT: Do NOT change VideoPlayer.Stretch here unless forced above. Reflect the current state.
            StretchModeButton.Visibility = canDisplayNone ? Visibility.Visible : Visibility.Collapsed; // Show button only if it fits
            if (isStretch)
            {
                StretchModeIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialDesignKind._1xMobiledata; // Icon for Uniform state
                                                                                                         // ストレッチ表示：親コンテナに合わせる
                VideoPlayer.Width = double.NaN;
                VideoPlayer.Height = double.NaN;
                VideoPlayer.Stretch = Stretch.Uniform; // サイズだけ固定で自然表示
            }
            else
            {
                StretchModeIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialDesignKind.ZoomOutMap; // Icon for None state
                                                                                                      // 1:1表示：DPIを考慮して元サイズを手動設定
                Size adjustedSize = GetDpiAdjustedSize(VideoPlayer.NaturalVideoWidth, VideoPlayer.NaturalVideoHeight);
                VideoPlayer.Width = adjustedSize.Width;
                VideoPlayer.Height = adjustedSize.Height;
                VideoPlayer.Stretch = Stretch.Uniform;  // サイズだけ固定で自然表示
            }
        }

        private void VideoPlayerContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateStretchMode();
        }

        private void VideoPlayer_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            LogHelper.LogError($"動画の再生に失敗しました: {e.ErrorException.Message}", e.ErrorException);
            MessageBox.Show($"動画の再生に失敗しました: {e.ErrorException.Message}", "再生エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            StopVideo();
            // 必要に応じてエラーイベントを外部に通知する
            // MediaFailed?.Invoke(this, e);
        }

        // UserControlがUnloadedされたときにリソースを解放
        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            // StopVideo() ではなく、直接 Stop() と Source=null を呼び出してリソースを解放
            VideoPlayer.Pause();
            VideoPlayer.Source = null;
            _seekBarUpdateTimer.Stop(); // タイマーも停止
            _seekBarUpdateTimer.Tick -= SeekBarUpdateTimer_Tick; // イベントハンドラ解除
        }

        private Size GetAvailableVideoSize()
        {
            // 動画専用のGrid.Row（動画表示領域）を取得
            if (VideoPlayer.Parent is FrameworkElement parent)
            {
                return GetDpiScaledSize(parent);
            }

            // フォールバックとして親Grid全体を取得
            return GetDpiScaledSize(VideoPlayerRoot);
        }

        private Size GetDpiAdjustedSize(double width, double height)
        {
            // 現在のDPIスケーリング倍率を取得
            PresentationSource source = PresentationSource.FromVisual(this);
            double dpiX = 1.0, dpiY = 1.0;

            if (source?.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }

            // DPI倍率を適用して正しいサイズを計算
            double adjustedWidth = width / dpiX;
            double adjustedHeight = height / dpiY;

            return new Size(adjustedWidth, adjustedHeight);
        }

        public static Size GetDpiScaledSize(FrameworkElement element)
        {
            if (element == null) return new Size(0, 0);

            // DPIスケーリング倍率を取得
            PresentationSource source = PresentationSource.FromVisual(element);
            double dpiX = 1.0, dpiY = 1.0;

            if (source?.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }

            // スケーリング後のサイズを計算
            // ActualWidth/Height が 0 の場合や dpi が 0 の場合も考慮 (通常は発生しないはず)
            double scaledWidth = (element.ActualWidth > 0 && dpiX > 0) ? element.ActualWidth * dpiX : 0;
            double scaledHeight = (element.ActualHeight > 0 && dpiY > 0) ? element.ActualHeight * dpiY : 0;

            return new Size(scaledWidth, scaledHeight);
        }


        // Helper to find visual child
        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T t)
                {
                    return t;
                }
                else
                {
                    T? childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }

        private void VideoControls_MouseEnter(object sender, MouseEventArgs e)
        {
            IsMouseOverControls = true;
        }

        private void VideoControls_MouseLeave(object sender, MouseEventArgs e)
        {
            IsMouseOverControls = false;
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VideoPlayer != null)
            {
                VideoPlayer.Volume = VolumeSlider.Value;
                // Slider操作時はミュート解除
                if (VolumeSlider.Value > 0)
                {
                    VideoPlayer.IsMuted = false;
                }
                UpdateVolumeIcon();

                // Save the new volume setting
                var settings = ViewerSettingsHelper.LoadSettings();
                settings.VideoVolume = VideoPlayer.Volume;
                ViewerSettingsHelper.SaveSettings(settings);
            }
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer != null)
            {
                VideoPlayer.IsMuted = !VideoPlayer.IsMuted;
                UpdateVolumeIcon();
                // ミュート状態に応じてスライダーの値も更新 (任意だがUX向上)
                if (VideoPlayer.IsMuted)
                {
                    // 必要であればミュート前の音量を保存しておくなどの処理を追加
                }
                else if (VolumeSlider.Value == 0 && VideoPlayer.Volume > 0)
                {
                    // ミュート解除時に音量が0だったらスライダーを元の音量に戻す (要改善: 元の音量を保持する必要あり)
                    // VolumeSlider.Value = VideoPlayer.Volume; // このままだと 0 のままの可能性
                }
            }
        }

        private void UpdateVolumeIcon()
        {
            if (VideoPlayer == null) return;

            // スライダーの値もミュート状態に応じて更新
            if (VideoPlayer.IsMuted)
            {
                VolumeIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialDesignKind.VolumeOff;
                // スライダーを無効化または見た目を変えることも検討
            }
            else
            {
                VolumeSlider.Value = VideoPlayer.Volume; // スライダーの値を現在の音量に合わせる
                if (VideoPlayer.Volume == 0)
                {
                    VolumeIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialDesignKind.VolumeOff;
                }
                else if (VideoPlayer.Volume > 0.7)
                {
                    VolumeIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialDesignKind.VolumeUp; // Changed from VolumeHigh
                }
                else if (VideoPlayer.Volume > 0.3)
                {
                    VolumeIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialDesignKind.VolumeDown; // Changed from VolumeMedium
                }
                else // Volume > 0 and <= 0.3
                {
                    VolumeIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialDesignKind.VolumeDown; // Changed from VolumeMinus, reusing VolumeDown
                }
            }
        }

        private void InitializeHideControlsTimer()
        {
            if (_hideControlsTimer == null)
            {
                _hideControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; // 3秒後に非表示
                _hideControlsTimer.Tick += HideControlsTimer_Tick;
            }
        }

        private void HideControlsTimer_Tick(object? sender, EventArgs e)
        {
            // Timer stops itself after ticking once (or stop it manually)
            _hideControlsTimer?.Stop();
            // Hide controls only if in full screen and mouse is not over controls
            if (IsFullScreen && !IsMouseOverControls)
            {
                VideoControls.Visibility = Visibility.Collapsed;
            }
        }

        private void VideoPlayerRoot_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (IsFullScreen)
            {
                var position = e.GetPosition(this);
                // Check if mouse is near the bottom where controls appear (using MinHeight as threshold)
                // Ensure ActualHeight is valid before calculation
                bool isMouseNearControls = this.ActualHeight > 0 && position.Y >= this.ActualHeight - VideoControls.MinHeight - 10; // Add a small buffer (10px)

                if (isMouseNearControls)
                {
                    // Show controls and reset the hide timer
                    VideoControls.Visibility = Visibility.Visible;
                    InitializeHideControlsTimer(); // Ensure timer is initialized
                    _hideControlsTimer?.Stop(); // Stop if already running
                    _hideControlsTimer?.Start(); // Start (or restart) the timer
                }
                else
                {
                    // If mouse moved outside the control area (but still within the UserControl),
                    // start the hide timer if controls are currently visible.
                    // This handles moving the mouse up from the control area.
                    if (VideoControls.Visibility == Visibility.Visible && !IsMouseOverControls) // Check IsMouseOverControls as well
                    {
                        InitializeHideControlsTimer();
                        _hideControlsTimer?.Stop();
                        _hideControlsTimer?.Start();
                    }
                }
            }
        }

        private void VideoPlayerRoot_MouseLeave(object sender, MouseEventArgs e)
        {
            // If the mouse leaves the control area while in full screen, start the hide timer
            // (but only if the controls are currently visible, otherwise the timer tick might hide them unexpectedly)
            if (IsFullScreen && VideoControls.Visibility == Visibility.Visible)
            {
                InitializeHideControlsTimer();
                _hideControlsTimer?.Stop();
                _hideControlsTimer?.Start();
            }
        }

        private void VideoPlayerRoot_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var originalSource = e.OriginalSource as DependencyObject;
            bool allowDoubleClick = false;

            // Case 1: Click is directly on the UserControl background
            if (originalSource == this)
            {
                allowDoubleClick = true;
            }
            else
            {
                // Case 2: Walk up the tree from the source
                DependencyObject? current = originalSource;
                while (current != null && current != this)
                {
                    // If we hit the VideoPlayer or its container first, allow it.
                    if (current == VideoPlayer || current == VideoPlayerContainer)
                    {
                        allowDoubleClick = true;
                        break; // Allowed, no need to check further up
                    }
                    // If we hit the VideoControls panel first, disallow it.
                    if (current == VideoControls)
                    {
                        allowDoubleClick = false;
                        break; // Disallowed, no need to check further up
                    }
                    current = VisualTreeHelper.GetParent(current);
                }
                // If the loop completes without hitting VideoControls or VideoPlayer/Container,
                // and the source wasn't 'this', it means the click was on some other element
                // within the UserControl but outside the explicitly allowed/disallowed areas.
                // In this case, we don't raise the event (allowDoubleClick remains false).
            }

            if (allowDoubleClick)
            {
                // Raise the custom routed event
                RaiseEvent(new RoutedEventArgs(BackgroundDoubleClickEvent, this));
                // Optionally mark the event as handled if the parent shouldn't process the standard double click further
                // e.Handled = true;
            }
        }
    }
}
