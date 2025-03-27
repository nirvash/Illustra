using System.Threading;
using System.Threading.Tasks;
using Prism.Events;
using Illustra.Shared.Models; // Updated namespace
// using Illustra.Services; // Removed - No longer needed
// using System.Windows.Threading; // Removed - No longer needed


namespace Illustra.MCPHost
{
    /// <summary>
    /// Handles communication between the Web API and the WPF application logic.
    /// Uses IEventAggregator to publish events that WPF ViewModels/Services subscribe to.
    /// </summary>
    // using Illustra.Services; is no longer needed
    public class APIService
    {
        private readonly IEventAggregator _eventAggregator;
        // private readonly IDispatcherService _dispatcherService; // Removed dependency

        public APIService(IEventAggregator eventAggregator) // Removed IDispatcherService injection
        {
            _eventAggregator = eventAggregator;
            // _dispatcherService = dispatcherService; // Removed assignment
        }

        /// <summary>
        /// ツールを非同期に実行し、結果を返します。
        /// </summary>
        /// <param name="toolName">実行するツール名</param>
        /// <param name="parameters">ツールのパラメータ</param>
        /// <returns>実行結果</returns>
        public virtual async Task<object> ExecuteToolAsync(string toolName, object parameters)
        {
            return await ExecuteToolAsync(toolName, parameters, CancellationToken.None);
        }

        /// <summary>
        /// ツールを非同期に実行し、結果を返します。
        /// </summary>
        /// <param name="toolName">実行するツール名</param>
        /// <param name="parameters">ツールのパラメータ</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>実行結果</returns>
        public virtual async Task<object> ExecuteToolAsync(string toolName, object parameters, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<object>();
            var args = new McpExecuteToolEventArgs // Renamed
            {
                ToolName = toolName,
                Parameters = parameters,
                ResultCompletionSource = tcs,
                CancellationToken = cancellationToken
            };

            // Dispatcherを使ってUIスレッドでPublish (必要に応じて)
            // await _dispatcher.InvokeAsync(() => _eventAggregator.GetEvent<McpExecuteToolEvent>().Publish(args));
            // 現状 Execute はUIスレッド不要かもしれないので直接Publish
            _eventAggregator.GetEvent<McpExecuteToolEvent>().Publish(args); // Renamed
            return await tcs.Task;
        }

        /// <summary>
        /// ツールに関する情報を非同期に取得します。
        /// </summary>
        /// <param name="toolName">情報を取得するツール名</param>
        /// <returns>取得した情報</returns>
        public virtual async Task<object> GetInfoAsync(string toolName)
        {
            return await GetInfoAsync(toolName, null, CancellationToken.None);
        }

        /// <summary>
        /// ツールに関する情報を非同期に取得します。
        /// </summary>
        /// <param name="toolName">情報を取得するツール名</param>
        /// <param name="filePath">関連するファイルパス</param>
        /// <returns>取得した情報</returns>
        public virtual async Task<object> GetInfoAsync(string toolName, string filePath)
        {
            return await GetInfoAsync(toolName, filePath, CancellationToken.None);
        }

        /// <summary>
        /// ツールに関する情報を非同期に取得します。
        /// </summary>
        /// <param name="toolName">情報を取得するツール名</param>
        /// <param name="filePath">関連するファイルパス</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>取得した情報</returns>
        public virtual async Task<object> GetInfoAsync(string toolName, string filePath, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<object>();
            var args = new McpGetInfoEventArgs // Renamed
            {
                ToolName = toolName,
                FilePath = filePath,
                ResultCompletionSource = tcs,
                CancellationToken = cancellationToken
            };

            // Dispatcherを使ってUIスレッドでPublish (必要に応じて)
            // await _dispatcher.InvokeAsync(() => _eventAggregator.GetEvent<McpGetInfoEvent>().Publish(args));
            // 現状 GetInfo はUIスレッド不要かもしれないので直接Publish
            _eventAggregator.GetEvent<McpGetInfoEvent>().Publish(args); // Renamed
            return await tcs.Task; // Return the task result
        }

        /// <summary>
        /// 指定されたフォルダを開くよう要求します。
        /// </summary>
        /// <param name="folderPath">開くフォルダのパス</param>
        /// <param name="sourceId">リクエスト元を識別するID</param>
        /// <returns>成功した場合はtrue、それ以外はfalse</returns>
        public virtual async Task<bool> OpenFolderAsync(string folderPath, string sourceId)
        {
            var tcs = new TaskCompletionSource<object>();
            var args = new McpOpenFolderEventArgs
            {
                FolderPath = folderPath,
                SourceId = sourceId,
                ResultCompletionSource = tcs
            };

            System.Diagnostics.Debug.WriteLine($"[APIService] Publishing McpOpenFolderEvent for path: {folderPath}, SourceId: {sourceId}");
            // Publish directly, handler will manage thread if needed
            _eventAggregator.GetEvent<McpOpenFolderEvent>().Publish(args);
            System.Diagnostics.Debug.WriteLine($"[APIService] Waiting for McpOpenFolderEvent result...");
            var result = await tcs.Task;
            System.Diagnostics.Debug.WriteLine($"[APIService] Received McpOpenFolderEvent result: {result}");
            return result is bool boolResult && boolResult;
        }

        /// <summary>
        /// 利用可能なツール（APIエンドポイント）のリストを取得します。
        /// </summary>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>利用可能なツールのリスト</returns>
        public virtual Task<object> GetAvailableToolsAsync(CancellationToken cancellationToken)
        {
            // TODO: 将来的にはリフレクションなどで動的に生成する
            var tools = new[]
            {
                new { Name = "execute", Method = "POST", Path = "/api/execute/{toolName}", Description = "Executes a specified tool with given parameters." },
                new { Name = "open_folder", Method = "POST", Path = "/api/commands/open_folder", Description = "Opens a specified folder in the Illustra application." },
                new { Name = "get_info", Method = "GET", Path = "/api/info/{toolName}", Description = "Gets information about a specific tool or lists available tools (use 'available_tools' as toolName)." }
                // 今後ツールが増えたらここに追加
            };
            return Task.FromResult<object>(tools);
        }
    }
}
