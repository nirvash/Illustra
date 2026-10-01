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
        private readonly SemaphoreSlim _requestLock = new(1, 1);

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

            await _requestLock.WaitAsync();
            try
            {
                if (args is not McpShutdownEventArgs)
                {
                    await PrepareTabAsync(args, timeout);
                }
                var result = await PublishCoreAsync(args, eventSelector, timeout);
                // タブ状態の変更通知後、サムネイルと選択の反映まで待つ。
                if (args is McpOpenFolderEventArgs && result is true)
                {
                    await PrepareTabAsync(args, timeout, waitOnly: true);
                }
                return result;
            }
            finally
            {
                _requestLock.Release();
            }
        }

        private async Task PrepareTabAsync(McpBaseEventArgs args, TimeSpan? timeout, bool waitOnly = false)
        {
            var prepare = new McpPrepareTabEventArgs
            {
                WaitOnly = waitOnly,
                TargetTab = args.TargetTab,
                ResolvedTabId = args.ResolvedTabId
            };
            await PublishCoreAsync(prepare, ea => ea.GetEvent<McpPrepareTabEvent>(), timeout);
            args.ResolvedTabId = prepare.ResolvedTabId;
        }

        private async Task<object?> PublishCoreAsync<TArgs>(
            TArgs args, Func<IEventAggregator, PubSubEvent<TArgs>> eventSelector, TimeSpan? timeout)
            where TArgs : McpBaseEventArgs
        {
            args.SourceId ??= SourceId;
            args.ResultCompletionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _dispatcher.InvokeAsync(() =>
            {
                if (args.ResolvedTabId.HasValue && args is not McpPrepareTabEventArgs)
                {
                    // 検証と発行を同じ UI 処理内で行い、間にユーザーのタブ変更を挟ませない。
                    var check = new McpPrepareTabEventArgs
                    {
                        ValidateOnly = true,
                        ResolvedTabId = args.ResolvedTabId,
                        ResultCompletionSource = new TaskCompletionSource<object>()
                    };
                    _eventAggregator.GetEvent<McpPrepareTabEvent>().Publish(check);
                    if (!check.ResultCompletionSource.Task.IsCompleted)
                        throw new InvalidOperationException("The tab validation handler is not available.");
                    check.ResultCompletionSource.Task.GetAwaiter().GetResult();
                }
                eventSelector(_eventAggregator).Publish(args);
            });
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
