using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.ViewModels;
using Prism.Events;
using Prism.Ioc;

namespace Illustra.Views
{
    /// <summary>
    /// ファイルシステムツリービューのコントロールクラス
    /// MVCパターンにおけるViewの役割を果たす
    /// </summary>
    public partial class FileSystemTreeView : UserControl
    {
        private FileSystemTreeViewModel _viewModel;
        private IEventAggregator _eventAggregator;
        private AppSettings _appSettings;

        public FileSystemTreeView()
        {
            InitializeComponent();
            Loaded += FileSystemTreeView_Loaded;
        }

        private void FileSystemTreeView_Loaded(object sender, RoutedEventArgs e)
        {
            // ViewModelの初期化とDataContextの設定
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();

            _appSettings = SettingsHelper.GetSettings();
            string? folderPath = _appSettings.LastFolderPath;
            if (!string.IsNullOrEmpty(folderPath) && !Directory.Exists(folderPath))
            {
                folderPath = null;
            }
            _viewModel = new FileSystemTreeViewModel(_eventAggregator, folderPath);
            DataContext = _viewModel;
        }

        // TreeViewの選択変更イベントハンドラ
        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is FileSystemItemModel item)
            {
                _viewModel.SelectedItem = item;
            }
        }

        // TreeViewItemの展開イベントハンドラ
        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem treeViewItem &&
                treeViewItem.DataContext is FileSystemItemModel item)
            {
                // ExpandItemCommandはUIスレッドで直接実行する
                if (_viewModel.ExpandItemCommand.CanExecute(item))
                {
                    _viewModel.ExpandItemCommand.Execute(item);
                }
            }
        }

        // パスを選択するためのパブリックメソッド
        public void Expand(string path)
        {
            if (_viewModel == null)
            {
                Debug.WriteLine("ViewModel is not initialized.");
                return;
            }
            _viewModel.Expand(path);
        }
    }
}
