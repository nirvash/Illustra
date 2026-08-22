using System.ComponentModel;
using System.Text.Json.Serialization;
using Illustra.Events;
using Illustra.Mcp;
using ModelContextProtocol.Server;

namespace Illustra.Mcp.Tools
{
    public record ShutdownResult(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("message")] string Message);

    public record AppStatusResult(
        [property: JsonPropertyName("currentFolder")] string? CurrentFolder,
        [property: JsonPropertyName("loadedFileCount")] int LoadedFileCount,
        [property: JsonPropertyName("selectedFiles")] IReadOnlyList<SelectedFileInfo> SelectedFiles,
        [property: JsonPropertyName("openTabs")] IReadOnlyList<string> OpenTabs);

    /// <summary>
    /// アプリケーション操作系ツール。
    /// </summary>
    [McpServerToolType]
    public class ApplicationTools
    {
        private readonly IMcpAppBridge _bridge;

        public ApplicationTools(IMcpAppBridge bridge)
        {
            _bridge = bridge;
        }

        [McpServerTool(Name = "shutdown_application")]
        [Description("Shuts down the Illustra application gracefully. Application state (settings, tabs, database) is persisted like a normal exit.")]
        public async Task<ShutdownResult> ShutdownApplication()
        {
            var args = new McpShutdownEventArgs();
            var result = await _bridge.PublishAndWaitAsync(args, ea => ea.GetEvent<McpShutdownEvent>(), TimeSpan.FromSeconds(10));

            if (result is not true)
            {
                throw new InvalidOperationException("Illustra did not accept the shutdown request.");
            }

            // レスポンスをクライアントへ返してから終了シーケンスが走るよう、ハンドラ側で遅延終了する。
            return new ShutdownResult(true, "Illustra is shutting down. State will be persisted.");
        }

        [McpServerTool(Name = "get_app_status", ReadOnly = true, Idempotent = true)]
        [Description("Returns the current application status of Illustra: the active tab folder, files loaded in the active view, currently selected files and open tab folders.")]
        public async Task<AppStatusResult> GetAppStatus()
        {
            var args = new McpGetAppStatusEventArgs();
            await _bridge.PublishAndWaitAsync(args, ea => ea.GetEvent<McpGetAppStatusEvent>());

            if (args.ErrorMessage is not null)
            {
                throw new InvalidOperationException($"Failed to get the application status: {args.ErrorMessage}");
            }

            return new AppStatusResult(
                args.CurrentFolder,
                args.LoadedFileCount,
                (args.SelectedFiles ?? []).Select(m => new SelectedFileInfo(m.Path, m.FileName)).ToList(),
                args.OpenTabs ?? []);
        }
    }
}
