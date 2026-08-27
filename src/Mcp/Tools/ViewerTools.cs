using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using Illustra.Events;
using Illustra.Mcp;
using ModelContextProtocol.Server;

namespace Illustra.Mcp.Tools
{
    public record ShowViewerResult(
        [property: JsonPropertyName("shown")] bool Shown,
        [property: JsonPropertyName("filePath")] string FilePath,
        [property: JsonPropertyName("visibleInCurrentFilter")] bool VisibleInCurrentFilter);

    public record CloseViewerResult(
        [property: JsonPropertyName("closed")] bool Closed,
        [property: JsonPropertyName("wasOpen")] bool WasOpen);

    /// <summary>
    /// ビューワの表示・終了ツール。UI 状況を変更するためブリッジ経路で実行する。
    /// </summary>
    [McpServerToolType]
    public class ViewerTools
    {
        private readonly IMcpAppBridge _bridge;

        public ViewerTools(IMcpAppBridge bridge)
        {
            _bridge = bridge;
        }

        [McpServerTool(Name = "show_viewer", Idempotent = true)]
        [Description("Shows a file in the Illustra image viewer window. When filePath is specified, it is selected in the active folder view (navigating to its parent folder when needed). If filePath is omitted, the currently selected file in the active folder view is used. The file is shown even when it is hidden by the current view filter; the response reports its visibility as visibleInCurrentFilter. Reuses the existing viewer window when it is already open. By default, forces the viewer to the front.")]
        public async Task<ShowViewerResult> ShowViewer(
            [Description("Absolute path of the image/video file to show. Omit to use the currently selected file.")] string filePath = "",
            [Description("When true, forces the viewer window to the front. Default true.")] bool bringToFront = true)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                var full = Path.GetFullPath(filePath);
                if (!File.Exists(full))
                {
                    throw new FileNotFoundException($"File not found: {full}");
                }

                filePath = full;
                await SelectSpecifiedFileAsync(filePath);
            }

            var args = new McpShowViewerEventArgs
            {
                FilePath = filePath,
                BringToFront = bringToFront
            };
            var result = await _bridge.PublishAndWaitAsync(args, ea => ea.GetEvent<McpShowViewerEvent>());

            if (result is not true)
            {
                throw new InvalidOperationException($"Failed to show the file in the viewer{(args.ErrorMessage is null ? "" : $": {args.ErrorMessage}")}");
            }

            return new ShowViewerResult(true, args.FilePath, args.VisibleInCurrentFilter);
        }

        [McpServerTool(Name = "close_viewer", Idempotent = true)]
        [Description("Closes the Illustra image viewer window if it is open.")]
        public async Task<CloseViewerResult> CloseViewer()
        {
            var args = new McpCloseViewerEventArgs();
            var result = await _bridge.PublishAndWaitAsync(args, ea => ea.GetEvent<McpCloseViewerEvent>());

            if (result is not true)
            {
                throw new InvalidOperationException($"Failed to close the viewer{(args.ErrorMessage is null ? "" : $": {args.ErrorMessage}")}");
            }

            return new CloseViewerResult(args.Closed, args.WasOpen);
        }

        private async Task SelectSpecifiedFileAsync(string filePath)
        {
            var targetFolder = Path.GetDirectoryName(filePath)
                ?? throw new ArgumentException($"Could not determine the parent folder: {filePath}", nameof(filePath));

            var statusArgs = new McpGetAppStatusEventArgs();
            await _bridge.PublishAndWaitAsync(statusArgs, ea => ea.GetEvent<McpGetAppStatusEvent>());

            if (!string.Equals(
                    Path.TrimEndingDirectorySeparator(statusArgs.CurrentFolder ?? string.Empty),
                    Path.TrimEndingDirectorySeparator(targetFolder),
                    StringComparison.OrdinalIgnoreCase))
            {
                var openFolderArgs = new McpOpenFolderEventArgs
                {
                    FolderPath = targetFolder,
                    SelectedFilePath = filePath
                };
                var openResult = await _bridge.PublishAndWaitAsync(openFolderArgs, ea => ea.GetEvent<McpOpenFolderEvent>());
                if (openResult is not true)
                {
                    throw new InvalidOperationException($"Illustra failed to open the folder: {targetFolder}");
                }

                return;
            }

            var selectArgs = new McpSelectFilesEventArgs { Paths = [filePath] };
            var selectResult = await _bridge.PublishAndWaitAsync(selectArgs, ea => ea.GetEvent<McpSelectFilesEvent>());
            if (selectResult is not int selectedCount || selectedCount != 1)
            {
                throw new InvalidOperationException($"Failed to select the file: {filePath}");
            }
        }

    }
}
