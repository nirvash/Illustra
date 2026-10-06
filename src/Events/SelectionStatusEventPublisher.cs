using Illustra.Models;
using Prism.Events;

namespace Illustra.Events
{
    /// <summary>確定した選択を通知する。ファイル通知を先に送り、件数通知を続ける。</summary>
    public static class SelectionStatusEventPublisher
    {
        public const string MainWindowSourceId = "MainWindow.ThumbnailList";
        private const string ThumbnailListSourceId = "ThumbnailList";

        public static void PublishFinalizedSelection(
            IEventAggregator eventAggregator,
            string? fullPath,
            int selectedCount,
            bool isMainWindowControl)
        {
            if (!string.IsNullOrWhiteSpace(fullPath))
            {
                eventAggregator.GetEvent<FileSelectedEvent>().Publish(
                    new SelectedFileModel(ThumbnailListSourceId, fullPath));
            }

            eventAggregator.GetEvent<SelectionCountChangedEvent>().Publish(
                new SelectionCountChangedEventArgs(
                    selectedCount,
                    isMainWindowControl ? MainWindowSourceId : ThumbnailListSourceId,
                    isMainWindowControl && selectedCount == 1 ? fullPath : null));
        }
    }
}
