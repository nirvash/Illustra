using System;
using System.Windows;
using System.Windows.Input;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.Mcp;
using Illustra.ViewModels;
using Illustra.ViewModels.Settings;
using Prism.Events;
using Prism.Ioc;

namespace Illustra.ViewModels.Settings
{
    /// <summary>
    /// MCP 設定セクションの ViewModel。
    /// 有効/無効は即時にホストへ反映し、ポート変更は「適用」で再起動して反映する。
    /// </summary>
    public class McpSettingsViewModel : SettingsViewModelBase
    {
        private const string TokenMaskChar = "●";

        private readonly AppSettingsModel _settings;
        private readonly McpHostManager _hostManager;

        private bool _enableServer;
        private string _portText = string.Empty;
        private bool _showToken;
        private bool _isBusy;

        public McpSettingsViewModel(AppSettingsModel settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _hostManager = ContainerLocator.Container.Resolve<McpHostManager>();

            ApplyPortCommand = new RelayCommand(ApplyPort, () => IsPortChanged && !_isBusy);
            CopyTokenCommand = new RelayCommand(CopyToken);
            RegenerateTokenCommand = new RelayCommand(RegenerateToken);

            // ホスト状態が変化したらステータス表示を更新（バックグラウンド発火のため UI スレッドで受信する）
            var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            eventAggregator.GetEvent<McpServerStatusChangedEvent>()
                .Subscribe(OnMcpServerStatusChanged, ThreadOption.UIThread);
        }

        /// <summary>
        /// MCP サーバーの有効/無効。変更は即時にホストへ反映される。
        /// </summary>
        public bool EnableServer
        {
            get => _enableServer;
            set
            {
                if (_isBusy)
                {
                    // 切替処理中は新しい値を採用せず、現在の状態へ表示を戻す
                    OnPropertyChanged(nameof(EnableServer));
                    return;
                }
                ApplyEnableStateAsync(value);
            }
        }

        /// <summary>リッスンポート（テキスト入力）。適用ボタンで保存・反映。</summary>
        public string PortText
        {
            get => _portText;
            set
            {
                if (_portText == value) return;
                _portText = value;
                OnPropertyChanged(nameof(PortText));
                OnPropertyChanged(nameof(IsPortChanged));
            }
        }

        /// <summary>ポート設定に未適用の変更があるか。</summary>
        public bool IsPortChanged =>
            int.TryParse(PortText, out var port) && port != _settings.McpPort;

        /// <summary>現在の稼働状態を示す表示文字列。</summary>
        public string StatusText
        {
            get
            {
                if (!_hostManager.IsRunning || string.IsNullOrEmpty(_hostManager.EndpointUrl))
                {
                    return GetLocalizedString("String_Settings_Mcp_StatusStopped", "Stopped");
                }

                return string.Format(
                    GetLocalizedString("String_Settings_Mcp_StatusRunning", "Running: {0}"),
                    _hostManager.EndpointUrl);
            }
        }

        /// <summary>トークンを平文表示するか。</summary>
        public bool ShowToken
        {
            get => _showToken;
            set
            {
                if (_showToken == value) return;
                _showToken = value;
                OnPropertyChanged(nameof(ShowToken));
                OnPropertyChanged(nameof(TokenDisplay));
                OnPropertyChanged(nameof(ShowHideTokenLabel));
            }
        }

        /// <summary>Bearer 認証を要求するか。変更は即時保存され、リクエスト毎に反映されるため再起動は不要。</summary>
        public bool RequireAuth
        {
            get => _settings.McpRequireAuth;
            set
            {
                if (_settings.McpRequireAuth == value) return;
                _settings.McpRequireAuth = value;
                SettingsHelper.SaveSettings(_settings);
                OnPropertyChanged(nameof(RequireAuth));
            }
        }

        /// <summary>表示切替ボタンのラベル。</summary>
        public string ShowHideTokenLabel =>
            ShowToken
                ? GetLocalizedString("String_Settings_Mcp_HideToken", "Hide")
                : GetLocalizedString("String_Settings_Mcp_ShowToken", "Show");

        /// <summary>トークン表示用文字列（非表示時はマスク）。</summary>
        public string TokenDisplay
        {
            get
            {
                var token = _settings.McpAccessToken ?? string.Empty;
                if (ShowToken)
                {
                    return token;
                }
                return string.IsNullOrEmpty(token)
                    ? string.Empty
                    : string.Concat(System.Linq.Enumerable.Repeat(TokenMaskChar, token.Length));
            }
        }

        public ICommand ApplyPortCommand { get; }
        public ICommand CopyTokenCommand { get; }
        public ICommand RegenerateTokenCommand { get; }

        /// <summary>
        /// トグル操作を即時反映する。失敗した場合は元の状態へ戻してユーザーに通知する。
        /// </summary>
        private async void ApplyEnableStateAsync(bool enable)
        {
            _isBusy = true;
            try
            {
                if (enable)
                {
                    await _hostManager.StartAsync();
                    _enableServer = true;
                }
                else
                {
                    await _hostManager.StopAsync();
                    _enableServer = false;
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError("MCP サーバーの状態切替中にエラーが発生しました", ex);

                // 起動失敗なら無効側、停止失敗なら有効側へ復帰
                _enableServer = !enable;

                MessageBox.Show(
                    string.Format(
                        GetLocalizedString("String_Settings_Mcp_ToggleError", "Failed to change MCP server state.\n{0}"),
                        ex.Message),
                    GetLocalizedString("String_Error", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isBusy = false;
            }

            OnPropertyChanged(nameof(EnableServer));
            OnPropertyChanged(nameof(StatusText));
        }

        /// <summary>MCP サーバーの稼働状態変化イベントを受け取る。</summary>
        private void OnMcpServerStatusChanged(McpServerStatusChangedEventArgs args)
        {
            OnPropertyChanged(nameof(StatusText));
        }

        /// <summary>
        /// ポート番号の変更を保存し、稼働中ならホストを再起動して反映する。
        /// </summary>
        private async void ApplyPort()
        {
            if (_isBusy || !ValidatePort(out var port)) return;

            var oldPort = _settings.McpPort;
            _isBusy = true;
            try
            {
                // ホストは SettingsHelper.GetSettings() 経由で最新のメモリ上のポートを参照するため、
                // 先にメモリ上の設定だけ更新して再起動する。永続化は再起動成功後に限定する。
                _settings.McpPort = port;

                if (_hostManager.IsRunning)
                {
                    await _hostManager.RestartAsync();
                }

                SettingsHelper.SaveSettings(_settings);
            }
            catch (Exception ex)
            {
                // 失敗時はメモリ上のポートを元に戻す（settings.json は未更新のまま）
                _settings.McpPort = oldPort;

                LogHelper.LogError("MCP ポートの適用中にエラーが発生しました", ex);
                MessageBox.Show(
                    string.Format(
                        GetLocalizedString("String_Settings_Mcp_ToggleError", "Failed to change MCP server state.\n{0}"),
                        ex.Message),
                    GetLocalizedString("String_Error", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isBusy = false;
            }

            OnPropertyChanged(nameof(IsPortChanged));
            OnPropertyChanged(nameof(StatusText));
        }

        private void CopyToken()
        {
            try
            {
                Clipboard.SetText(_settings.McpAccessToken ?? string.Empty);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("アクセストークンのコピー中にエラーが発生しました", ex);
            }
        }

        /// <summary>
        /// アクセストークンを再生成して保存する。
        /// 検証はリクエスト毎に現在値を参照するため、サーバー再起動は不要。
        /// </summary>
        private void RegenerateToken()
        {
            _settings.McpAccessToken = McpAccessTokenGenerator.Generate();
            SettingsHelper.SaveSettings(_settings);
            OnPropertyChanged(nameof(TokenDisplay));
        }

        private bool ValidatePort(out int port)
        {
            if (!int.TryParse(PortText, out port) || port < 1 || port > 65535)
            {
                MessageBox.Show(
                    GetLocalizedString("String_Settings_Mcp_PortInvalid", "Port must be an integer between 1 and 65535."),
                    GetLocalizedString("String_Error", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
            return true;
        }

        private static string GetLocalizedString(string key, string defaultValue)
        {
            var value = Application.Current?.TryFindResource(key) as string;
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        public override void LoadSettings()
        {
            _enableServer = _hostManager.IsRunning;
            PortText = _settings.McpPort > 0
                ? _settings.McpPort.ToString()
                : new AppSettingsModel().McpPort.ToString();
            ShowToken = false;

            OnPropertyChanged(nameof(EnableServer));
            OnPropertyChanged(nameof(RequireAuth));
            OnPropertyChanged(nameof(TokenDisplay));
            OnPropertyChanged(nameof(IsPortChanged));
            OnPropertyChanged(nameof(StatusText));
        }

        public override void SaveSettings()
        {
            // ポートは「適用」成功時のみ保存するため、ここではトグル状態の整合性維持のみ行う
            _settings.EnableMcpHost = _enableServer;
        }

        public override bool ValidateSettings()
        {
            return int.TryParse(PortText, out var port) && port >= 1 && port <= 65535;
        }
    }
}
