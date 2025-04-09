using Illustra.Helpers;
using System.Threading.Tasks;
using System.Windows.Controls;
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
                DataContext = _viewModel;
                Unloaded += WebpPlayerControl_Unloaded;
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

        private void WebpPlayerControl_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_viewModel != null)
                {
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
