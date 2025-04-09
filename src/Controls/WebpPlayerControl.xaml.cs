using Illustra.Helpers;
using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Illustra.ViewModels;
using Illustra.Services;

using System.Windows.Media;
using System.Windows; // VisualTreeHelperのため

namespace Illustra.Controls
{
    public partial class WebpPlayerControl : UserControl
    {
        // Routed event for background double click
        public static readonly RoutedEvent BackgroundDoubleClickEvent =
            EventManager.RegisterRoutedEvent("BackgroundDoubleClick", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(WebpPlayerControl));

        // CLR event wrapper
        public event RoutedEventHandler BackgroundDoubleClick
        {
            add { AddHandler(BackgroundDoubleClickEvent, value); }
            remove { RemoveHandler(BackgroundDoubleClickEvent, value); }
        }

        private readonly WebpPlayerViewModel _viewModel;

        public WebpPlayerControl()
        {
            try
            {
                InitializeComponent();
                _viewModel = new WebpPlayerViewModel(new WebpAnimationService());
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                DataContext = _viewModel;
                Unloaded += WebpPlayerControl_Unloaded;
                UpdatePlayPauseButtonVisibility(_viewModel.CurrentState); // 初期状態を設定
                UpdateRepeatButtonVisualState(_viewModel.IsRepeatEnabled); // リピートボタンの初期状態を設定
                LogHelper.LogWithTimestamp("WebPプレイヤーコントロールを初期化しました。", LogHelper.Categories.Performance);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("WebPプレイヤーコントロールの初期化に失敗しました。", ex);
                throw;
            }
        }

        public async Task LoadWebpAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));
            }

            try
            {
                LogHelper.LogWithTimestamp($"WebPファイルの読み込みを開始: {filePath}", LogHelper.Categories.Performance);
                await _viewModel.LoadAsync(filePath);
                LogHelper.LogWithTimestamp($"WebPファイルの読み込みが完了しました: {filePath}", LogHelper.Categories.Performance);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"WebPファイルの読み込みに失敗しました: {filePath}", ex);
                throw;
            }
        }

        /// <summary>
        /// WebPアニメーションの再生を停止します
        /// </summary>
        public void Stop()
        {
            if (_viewModel.CurrentState == PlayState.Playing)
            {
                _viewModel.PlayPause();
            }
        }

        private void WebpPlayerRoot_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var originalSource = e.OriginalSource as DependencyObject;
            bool allowDoubleClick = false; // Default to disallow, explicitly allow later
            bool controlsHit = false;

            DependencyObject? current = originalSource;

            // Walk up the tree to see if the click originated from the ControlsPanel
            while (current != null && current != this)
            {
                if (current is FrameworkElement fe && fe.Name == "ControlsPanel")
                {
                    controlsHit = true;
                    break; // Found controls, stop searching
                }
                current = VisualTreeHelper.GetParent(current);
            }

            // If the click did NOT originate from the controls panel, then allow it.
            // This covers clicks on the background (originalSource == this)
            // and clicks on the image (or its container, as long as it's not ControlsPanel).
            if (!controlsHit)
            {
                allowDoubleClick = true;
            }

            // Final decision based on the check
            if (allowDoubleClick)
            {
                // Raise the custom event if allowed
                RaiseEvent(new RoutedEventArgs(BackgroundDoubleClickEvent, this));
            }
            else
            {
                // Handle the event (prevent bubbling) if it was on the controls
                e.Handled = true;
            }
        }

        private void SeekBarContainer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ignore clicks directly on the Thumb (handled by Slider's built-in drag)
            if (e.OriginalSource is Thumb)
            {
                return;
            }

            // Handle clicks on the track part
            // (ViewModel_PropertyChanged と UpdatePlayPauseButtonVisibility はクラスレベルに移動済み)
            if (sender is Border container && SeekBar.IsEnabled)
            {
                _viewModel?.SeekBarStarted();

                // Find the Thumb and simulate a MouseLeftButtonDown event on it
                var thumb = FindVisualChild<Thumb>(SeekBar);
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
            }
        }

        private void SeekBarContainer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // マウスキャプチャを解放
            if (Mouse.Captured is Thumb capturedThumb && capturedThumb == FindVisualChild<Thumb>(SeekBar))
            {
                Mouse.Capture(null);
            }

            _viewModel?.SeekBarCompleted();
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

        private void SeekBar_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _viewModel?.SeekBarStarted();
        }

        private void SeekBar_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _viewModel?.SeekBarCompleted();
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WebpPlayerViewModel.CurrentState))
            {
                UpdatePlayPauseButtonVisibility(_viewModel.CurrentState);
            }
            else if (e.PropertyName == nameof(WebpPlayerViewModel.IsRepeatEnabled))
            {
                UpdateRepeatButtonVisualState(_viewModel.IsRepeatEnabled);
            }
        }

        private void UpdatePlayPauseButtonVisibility(PlayState state)
        {
            if (state == PlayState.Playing)
            {
                PlayButton.Visibility = Visibility.Collapsed;
                PauseButton.Visibility = Visibility.Visible;
            }
            else
            {
                PlayButton.Visibility = Visibility.Visible;
                PauseButton.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateRepeatButtonVisualState(bool isRepeatEnabled)
        {
            // リピート状態に応じてボタンの背景色などを変更
            // XAMLでRepeatButtonにx:Name="RepeatButton"が設定されている前提
            var repeatButton = FindName("RepeatButton") as Button;
            if (repeatButton != null)
            {
                repeatButton.Background = isRepeatEnabled ? Brushes.RoyalBlue : Brushes.Transparent;
            }
        }

        private void WebpPlayerControl_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged -= ViewModel_PropertyChanged; // イベントハンドラ解除
                    _viewModel.Dispose();
                    LogHelper.LogWithTimestamp("ViewModelのリソースを解放しました。", LogHelper.Categories.Performance);
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError("ViewModelのリソース解放中にエラーが発生しました。", ex);
            }
            finally
            {
                Unloaded -= WebpPlayerControl_Unloaded;
            }
        }

    }
}
