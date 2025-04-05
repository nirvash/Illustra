using Illustra.Functions; // Added for KeyboardShortcutHandler and FuncId

using Illustra.Shared.Models.Tools; // Added for McpOpenFolderEventArgs
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.Shared.Models; // Added for MCP events
using Prism.Ioc;

namespace Illustra.Views
{
    public partial class FolderTreeControl : UserControl, IActiveAware
    {
        private IEventAggregator? _eventAggregator;
        private string? _currentSelectedFilePath = string.Empty;
        private bool ignoreSelectedChangedOnce;
        private const string CONTROL_ID = "FolderTree";

        #region IActiveAware Implementation
#pragma warning disable 0067 // 使用されていませんという警告を無視
        public bool IsActive { get; set; }
        public event EventHandler? IsActiveChanged;
#pragma warning restore 0067 // 警告の無視を終了
        #endregion

        // xaml でインスタンス化するためのデフォルトコンストラクタ
        public FolderTreeControl()
        {
            InitializeComponent();
            Loaded += FolderTreeControl_Loaded;

            // キーボードイベントハンドラを追加
            PreviewKeyDown += FolderTreeControl_PreviewKeyDown;

            // TextBoxのイベントハンドラを追加
            _addressBox.GotFocus += AddressBox_GotFocus;
        }

        private void AddressBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.Dispatcher.BeginInvoke(new Action(() =>
                {
                    textBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void ScrollAddressBoxToEnd()
        {
            Dispatcher.BeginInvoke(new Action(() => _addressBox.ScrollToHorizontalOffset(_addressBox.ActualWidth)));
        }

        private void UpdateToolTip(string path)
        {
            var tooltipFormat = (string)Application.Current.Resources["String_FolderTree_Tooltip"];
            _addressBox!.ToolTip = string.Format(tooltipFormat, path);
        }

        private void FolderTreeControl_Loaded(object sender, RoutedEventArgs e)
        {
            // ContainerLocatorを使ってEventAggregatorを取得
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<McpOpenFolderEvent>().Subscribe(OnMcpFolderSelected, ThreadOption.UIThread, false, // Renamed
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視

            // 初期値を設定
            var settings = SettingsHelper.GetSettings();
            if (!string.IsNullOrEmpty(settings.LastFolderPath))
            {
                _currentSelectedFilePath = settings.LastFolderPath;
                _addressBox.Text = settings.LastFolderPath;
                ScrollAddressBoxToEnd();
                UpdateToolTip(settings.LastFolderPath);
            }
        }

        private void FolderTreeControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // TextBoxがフォーカスを持っている場合は無視
            if (e.OriginalSource is TextBox)
            {
                return;
            }

            // ショートカットキーハンドラを取得
            var shortcutHandler = KeyboardShortcutHandler.Instance;

            // コピー (Ctrl+C)
            if (shortcutHandler.IsShortcutMatch(FuncId.Copy, e.Key))
            {
                _eventAggregator?.GetEvent<ShortcutKeyEvent>().Publish(new ShortcutKeyEventArgs { Key = e.Key, Modifiers = Keyboard.Modifiers, SourceId = CONTROL_ID });
                e.Handled = true;
            }
            // 貼り付け (Ctrl+V)
            else if (shortcutHandler.IsShortcutMatch(FuncId.Paste, e.Key))
            {
                _eventAggregator?.GetEvent<ShortcutKeyEvent>().Publish(new ShortcutKeyEventArgs { Key = e.Key, Modifiers = Keyboard.Modifiers, SourceId = CONTROL_ID });
                e.Handled = true;
            }
            // 切り取り (Ctrl+X) - Note: FuncIdにCutがない場合は追加が必要
            // else if (shortcutHandler.IsShortcutMatch(FuncId.Cut, e.Key)) // FuncId.Cut が存在する場合
            // {
            //     _eventAggregator?.GetEvent<ShortcutKeyEvent>().Publish(new ShortcutKeyEventArgs { Key = e.Key, Modifiers = Keyboard.Modifiers, SourceId = CONTROL_ID });
            //     e.Handled = true;
            // }
            // すべて選択 (Ctrl+A)
            else if (shortcutHandler.IsShortcutMatch(FuncId.SelectAll, e.Key))
            {
                _eventAggregator?.GetEvent<ShortcutKeyEvent>().Publish(new ShortcutKeyEventArgs { Key = e.Key, Modifiers = Keyboard.Modifiers, SourceId = CONTROL_ID });
                e.Handled = true;
            }
            // 削除 (Delete)
            else if (shortcutHandler.IsShortcutMatch(FuncId.Delete, e.Key))
            {
                _eventAggregator?.GetEvent<ShortcutKeyEvent>().Publish(new ShortcutKeyEventArgs { Key = e.Key, Modifiers = Keyboard.Modifiers, SourceId = CONTROL_ID });
                e.Handled = true;
            }
            // 追加: リストの先頭/末尾への移動ショートカット
            else if (shortcutHandler.IsShortcutMatch(FuncId.MoveToStart, e.Key))
            {
                _eventAggregator?.GetEvent<ShortcutKeyEvent>().Publish(new ShortcutKeyEventArgs { Key = e.Key, Modifiers = Keyboard.Modifiers, SourceId = CONTROL_ID });
                e.Handled = true;
            }
            else if (shortcutHandler.IsShortcutMatch(FuncId.MoveToEnd, e.Key))
            {
                _eventAggregator?.GetEvent<ShortcutKeyEvent>().Publish(new ShortcutKeyEventArgs { Key = e.Key, Modifiers = Keyboard.Modifiers, SourceId = CONTROL_ID });
                e.Handled = true;
            }
        }

        private void AddressBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox textBox)
            {
                e.Handled = true;
                var path = textBox.Text.Trim();

                try
                {
                    if (!Directory.Exists(path))
                    {
                        MessageBox.Show(
                            (string)Application.Current.Resources["String_FolderTree_NotFound"],
                            (string)Application.Current.Resources["String_Error"],
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    // アクセス権のチェック
                    try
                    {
                        Directory.GetFiles(path);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        MessageBox.Show(
                            (string)Application.Current.Resources["String_FolderTree_AccessDenied"],
                            (string)Application.Current.Resources["String_Error"],
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    _eventAggregator?.GetEvent<McpOpenFolderEvent>().Publish( // Renamed
                        new McpOpenFolderEventArgs // Renamed
                        {
                            FolderPath = path,
                            SourceId = CONTROL_ID,
                            ResultCompletionSource = null // No need to wait for result from UI interaction
                        });
                }
                catch (Exception)
                {
                    MessageBox.Show(
                        (string)Application.Current.Resources["String_FolderTree_InvalidPath"],
                        (string)Application.Current.Resources["String_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        private void OnMcpFolderSelected(McpOpenFolderEventArgs args) // Renamed and changed args type
        {
            if (args.FolderPath == _currentSelectedFilePath) return; // Changed property name
            if (ignoreSelectedChangedOnce)
            {
                ignoreSelectedChangedOnce = false;
                return;
            }

            ignoreSelectedChangedOnce = false;
            _currentSelectedFilePath = args.FolderPath; // Changed property name
            _addressBox.Text = args.FolderPath; // Changed property name
            ScrollAddressBoxToEnd();
            UpdateToolTip(args.FolderPath); // Changed property name

            // Expand 処理は FileSystemTreeViewControl が行うのでここでは行わない
            _eventAggregator?.GetEvent<SelectFileRequestEvent>().Publish("");
        }

        public void SetCurrentSettings()
        {
            var appSettings = SettingsHelper.GetSettings();
            appSettings.LastFolderPath = _currentSelectedFilePath;
        }
    }
}
