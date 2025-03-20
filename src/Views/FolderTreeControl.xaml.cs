using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Models;

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
        }

        private void FolderTreeControl_Loaded(object sender, RoutedEventArgs e)
        {
            // ContainerLocatorを使ってEventAggregatorを取得
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視
        }

        private void FolderTreeControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
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
