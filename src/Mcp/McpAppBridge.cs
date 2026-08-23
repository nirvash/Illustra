using System.Windows.Threading;
using Illustra.Events;
using Prism.Events;

namespace Illustra.Mcp
{
    /// <summary>
    /// MCP ツールから WPF アプリの状態・UI を操作するためのブリッジ。
    /// EventAggregator + TaskCompletionSource パターンを共通化する。
    /// </summary>
    public interface IMcpAppBridge
    {
        /// <summary>
        /// リクエストイベントを発行し、UI 側ハンドラによる完了通知を待機する。
        /// </summary>
        /// <param name="args">リクエスト引数（SourceId / ResultCompletionSource はここで設定される）</param>
        /// <param name="eventSelector">発行するイベント型の選択</param>
        /// <param name="timeout">タイムアウト（既定 30 秒）</param>
        Task<object?> PublishAndWaitAsync<TArgs>(
            TArgs args,
            Func<IEventAggregator, PubSubEvent<TArgs>> eventSelector,
            TimeSpan? timeout = null)
            where TArgs : McpBaseEventArgs;

        /// <summary>
        /// UI スレッドでアクションを実行し完了を待機する。
        /// </summary>
        Task InvokeOnUiThreadAsync(Action action);
    }

    public class McpAppBridge : IMcpAppBridge
    {
        public const string SourceId = "mcp-v2-tool";
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        private readonly IEventAggregator _eventAggregator;
        private readonly Dispatcher _dispatcher;

        public McpAppBridge(IEventAggregator eventAggregator, Dispatcher dispatcher)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public async Task<object?> PublishAndWaitAsync<TArgs>(
            TArgs args,
            Func<IEventAggregator, PubSubEvent<TArgs>> eventSelector,
            TimeSpan? timeout = null)
            where TArgs : McpBaseEventArgs
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            args.SourceId ??= SourceId;
            args.ResultCompletionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            // イベントは UI スレッド購読前提のため、発行も UI スレッドへマーシャリングする
            await _dispatcher.InvokeAsync(() => eventSelector(_eventAggregator).Publish(args));

            try
            {
                return await args.ResultCompletionSource.Task.WaitAsync(timeout ?? DefaultTimeout);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"MCP request timed out ({(timeout ?? DefaultTimeout).TotalSeconds}s): {typeof(TArgs).Name}");
            }
        }

        public Task InvokeOnUiThreadAsync(Action action)
        {
            return _dispatcher.InvokeAsync(action).Task;
        }
    }
}
