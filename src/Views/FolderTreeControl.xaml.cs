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
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected, ThreadOption.UIThread, false,
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

            // Ctrlキーが押されているかチェック
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                bool handled = false;

                // Ctrl+C (コピー)
                if (e.Key == Key.C)
                {
                    _eventAggregator?.GetEvent<ShortcutKeyEvent>().Publish(new ShortcutKeyEventArgs { Key = Key.C, Modifiers = ModifierKeys.Control, SourceId = CONTROL_ID });
                    handled = true;
                }
                // Ctrl+V (貼り付け)
                else if (e.Key == Key.V)
                {
                    _eventAggregator?.GetEvent<ShortcutKeyEvent>().Publish(new ShortcutKeyEventArgs { Key = Key.V, Modifiers = ModifierKeys.Control, SourceId = CONTROL_ID });
                    handled = true;
                }
                // Ctrl+X (切り取り)
                else if (e.Key == Key.X)
                {
                    _eventAggregator?.GetEvent<ShortcutKeyEvent>().Publish(new ShortcutKeyEventArgs { Key = Key.X, Modifiers = ModifierKeys.Control, SourceId = CONTROL_ID });
                    handled = true;
                }
                // Ctrl+A (すべて選択)
                else if (e.Key == Key.A)
                {
                    _eventAggregator?.GetEvent<ShortcutKeyEvent>().Publish(new ShortcutKeyEventArgs { Key = Key.A, Modifiers = ModifierKeys.Control, SourceId = CONTROL_ID });
                    handled = true;
                }

                if (handled)
                {
                    e.Handled = true;
                }
            }
            // Deleteキー
            else if (e.Key == Key.Delete)
            {
                _eventAggregator?.GetEvent<ShortcutKeyEvent>().Publish(new ShortcutKeyEventArgs { Key = Key.Delete, SourceId = CONTROL_ID });
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

                    _eventAggregator?.GetEvent<FolderSelectedEvent>().Publish(
                        new FolderSelectedEventArgs(path, CONTROL_ID));
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

        private void OnFolderSelected(FolderSelectedEventArgs args)
        {
            if (args.Path == _currentSelectedFilePath) return;
            if (ignoreSelectedChangedOnce)
            {
                ignoreSelectedChangedOnce = false;
                return;
            }

            ignoreSelectedChangedOnce = false;
            _currentSelectedFilePath = args.Path;
            _addressBox.Text = args.Path;
            ScrollAddressBoxToEnd();
            UpdateToolTip(args.Path);

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
