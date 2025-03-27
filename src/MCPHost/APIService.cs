using System.Threading;
using System.Threading.Tasks;
using Prism.Events;
using Illustra.MCPHost.Events;

namespace Illustra.MCPHost
{
    /// <summary>
    /// Handles communication between the Web API and the WPF application logic.
    /// Uses IEventAggregator to publish events that WPF ViewModels/Services subscribe to.
    /// </summary>
    public class APIService
    {
        private readonly IEventAggregator _eventAggregator;

        public APIService(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
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
            var args = new ExecuteToolEventArgs
            {
                ToolName = toolName,
                Parameters = parameters,
                ResultCompletionSource = tcs,
                CancellationToken = cancellationToken
            };

            _eventAggregator.GetEvent<ExecuteToolEvent>().Publish(args);
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
            var args = new GetInfoEventArgs
            {
                ToolName = toolName,
                FilePath = filePath,
                ResultCompletionSource = tcs,
                CancellationToken = cancellationToken
            };

            _eventAggregator.GetEvent<GetInfoEvent>().Publish(args);
            return await tcs.Task;
        }
    }
}
