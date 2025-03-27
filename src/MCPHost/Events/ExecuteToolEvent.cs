using System.Threading;
using System.Threading.Tasks;
using Prism.Events;

namespace Illustra.MCPHost.Events
{
    /// <summary>
    /// ツール実行要求を表すイベント
    /// </summary>
    public class ExecuteToolEvent : PubSubEvent<ExecuteToolEventArgs>
    {
    }

    /// <summary>
    /// ツール実行要求のパラメータを表すイベント引数
    /// </summary>
    public class ExecuteToolEventArgs
    {
        /// <summary>
        /// 実行するツール名
        /// </summary>
        public string ToolName { get; set; } = string.Empty;

        /// <summary>
        /// ツールのパラメータ
        /// </summary>
        public object Parameters { get; set; } = new object();

        /// <summary>
        /// 実行結果を返すための TaskCompletionSource
        /// </summary>
        public TaskCompletionSource<object> ResultCompletionSource { get; set; } = new TaskCompletionSource<object>();

        /// <summary>
        /// キャンセルトークン
        /// </summary>
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
    }
}
