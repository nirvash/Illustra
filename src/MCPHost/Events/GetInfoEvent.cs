using System.Threading;
using System.Threading.Tasks;
using Prism.Events;

namespace Illustra.MCPHost.Events
{
    /// <summary>
    /// 情報取得要求を表すイベント
    /// </summary>
    public class GetInfoEvent : PubSubEvent<GetInfoEventArgs>
    {
    }

    /// <summary>
    /// 情報取得要求のパラメータを表すイベント引数
    /// </summary>
    public class GetInfoEventArgs
    {
        /// <summary>
        /// 情報を取得するツール名
        /// </summary>
        public string ToolName { get; set; } = string.Empty;

        /// <summary>
        /// 関連するファイルパス（オプション）
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 取得結果を返すための TaskCompletionSource
        /// </summary>
        public TaskCompletionSource<object> ResultCompletionSource { get; set; } = new TaskCompletionSource<object>();

        /// <summary>
        /// キャンセルトークン
        /// </summary>
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
    }
}
