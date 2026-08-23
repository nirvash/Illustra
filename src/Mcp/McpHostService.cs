using System.Reflection;
using System.Windows.Threading;
using Illustra.Helpers;
using Illustra.Mcp;
using Illustra.Mcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Prism.Events;

namespace Illustra.Mcp
{
    /// <summary>
    /// MCP v2 サーバー（Streamable HTTP）のホスト。
    /// アプリ内 Kestrel で localhost のみリッスンし、/mcp エンドポイントを公開する。
    /// </summary>
    public class McpHostService
    {
        private const int DefaultPort = 5149;

#if DEBUG
        /// <summary>
        /// 開発版 (Debug ビルド) がインストール済みリリース版と並行起動してもポートが競合しないよう加算するオフセット。
        /// </summary>
        public const int DebugPortOffset = 10;
#endif

        private readonly IEventAggregator _eventAggregator;
        private readonly Dispatcher _dispatcher;
        private readonly DatabaseManager _dbManager;
        private readonly int _port;
        private readonly Func<string?> _tokenProvider;

        private WebApplication? _app;

        public bool IsRunning { get; private set; }
        public string EndpointUrl => $"http://localhost:{_port}/mcp";

        public McpHostService(
            IEventAggregator eventAggregator,
            Dispatcher dispatcher,
            DatabaseManager dbManager,
            int port,
            Func<string?> tokenProvider)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _dbManager = dbManager ?? throw new ArgumentNullException(nameof(dbManager));
            _port = port > 0 ? port : DefaultPort;
#if DEBUG
            _port += DebugPortOffset;
#endif
            _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        }

        /// <summary>
        /// ホストを起動する。失敗時は例外をスローする（呼び出し側でログして続行）。
        /// </summary>
        public async Task StartAsync()
        {
            if (IsRunning) return;

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders(); // WPF 側のログ体系を使うため既定ロガーを無効化
            builder.WebHost.UseUrls($"http://localhost:{_port}");

            var bridge = new McpAppBridge(_eventAggregator, _dispatcher);
            builder.Services.AddSingleton<IMcpAppBridge>(bridge);
            builder.Services.AddSingleton(_eventAggregator);
            builder.Services.AddSingleton(_dbManager);
            builder.Services.AddSingleton<FolderTools>();
            builder.Services.AddSingleton<FileSelectionTools>();
            builder.Services.AddSingleton<MetadataTools>();
            builder.Services.AddSingleton<FileOperationTools>();
            builder.Services.AddSingleton<ApplicationTools>();

            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

            builder.Services.AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation
                    {
                        Name = "Illustra",
                        Version = version
                    };
                    options.ServerInstructions =
                        "Illustra is a Windows image viewer with ComfyUI/StableDiffusion generation metadata support. " +
                        "Use list_files to browse the active folder, get_thumbnail to visually inspect an image, " +
                        "get_file_metadata to read generation prompts and rating, select_file to select files in the UI, " +
                        "and move_files/copy_files/create_folder for file organization.";
                })
                .WithHttpTransport()
                .WithToolsFromAssembly(typeof(McpHostService).Assembly);

            var app = builder.Build();
            app.UseMiddleware<BearerTokenMiddleware>(_tokenProvider);
            app.MapMcp("/mcp");

            await app.StartAsync().ConfigureAwait(false);
            _app = app;
            IsRunning = true;
        }

        /// <summary>
        /// ホストを停止する。停止・破棄の各段階にタイムアウトを設け、アプリ終了処理がブロックされないようにする。
        /// </summary>
        public async Task StopAsync(TimeSpan? timeout = null)
        {
            if (!IsRunning || _app == null) return;

            var app = _app;
            _app = null;
            IsRunning = false;

            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);

            try
            {
                using var cts = new CancellationTokenSource(effectiveTimeout);
                await app.StopAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 未完了の接続がある場合は中断されてここに到達する。破棄へ進む
                LogHelper.LogWithTimestamp($"MCP ホストの停止がタイムアウトしました（{effectiveTimeout.TotalSeconds}s）。強制破棄へ進みます", LogHelper.Categories.MCP);
            }
            finally
            {
                try
                {
                    await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (TimeoutException ex)
                {
                    LogHelper.LogError("MCP ホストの DisposeAsync がタイムアウトしました。破棄を諦めて続行します", ex);
                }
            }
        }
    }
}
