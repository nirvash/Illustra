using System;
using System.Windows.Threading;
using Illustra.Events;
using Illustra.Helpers;
using Prism.Events;

namespace Illustra.Mcp
{
    /// <summary>
    /// MCP ホストのライフサイクルを管理するシングルトン。
    /// アプリ起動時の自動開始と、設定画面からの動的な開始/停止/再起動を担う。
    /// </summary>
    public class McpHostManager
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly Dispatcher _dispatcher;
        private readonly DatabaseManager _dbManager;
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);

        private McpHostService? _host;

        public bool IsRunning { get; private set; }

        public string? EndpointUrl => _host?.EndpointUrl;

        public McpHostManager(IEventAggregator eventAggregator, Dispatcher dispatcher, DatabaseManager dbManager)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _dbManager = dbManager ?? throw new ArgumentNullException(nameof(dbManager));
        }

        /// <summary>
        /// 現在の設定値で MCP ホストを起動する。アクセストークンが未生成なら自動生成して保存する。
        /// </summary>
        public async Task StartAsync()
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsRunning) return;

                var settings = SettingsHelper.GetSettings();
                if (string.IsNullOrEmpty(settings.McpAccessToken))
                {
                    settings.McpAccessToken = McpAccessTokenGenerator.Generate();
                    SettingsHelper.SaveSettings(settings);
                }

                var host = new McpHostService(
                    _eventAggregator,
                    _dispatcher,
                    _dbManager,
                    settings.McpPort,
                    () => SettingsHelper.GetSettings().McpAccessToken);

                await host.StartAsync().ConfigureAwait(false);

                _host = host;
                IsRunning = true;
            }
            finally
            {
                _lifecycleLock.Release();
            }

            PublishStatus();
        }

        /// <summary>
        /// MCP ホストを停止する。未起動の場合は何もしない。
        /// </summary>
        public async Task StopAsync(TimeSpan? timeout = null)
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var host = _host;
                if (host == null) return;
                _host = null;
                IsRunning = false;

                try
                {
                    await host.StopAsync(timeout).ConfigureAwait(false);
                }
                finally
                {
                    PublishStatus();
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>
        /// MCP ホストを再起動する（ポート変更の反映用）。
        /// </summary>
        public async Task RestartAsync()
        {
            await StopAsync().ConfigureAwait(false);
            await StartAsync().ConfigureAwait(false);
        }

        private void PublishStatus()
        {
            _eventAggregator.GetEvent<McpServerStatusChangedEvent>().Publish(new McpServerStatusChangedEventArgs
            {
                IsRunning = IsRunning,
                EndpointUrl = EndpointUrl
            });
        }
    }
}
