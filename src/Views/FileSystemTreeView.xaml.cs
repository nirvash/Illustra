using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.ViewModels;

namespace Illustra.Views
{
    /// <summary>
    /// ファイルシステムツリービューのコントロールクラス
    /// MVCパターンにおけるViewの役割を果たす
    /// </summary>
    public partial class FileSystemTreeView : UserControl
    {
        private FileSystemTreeViewModel? _viewModel;
        private IEventAggregator? _eventAggregator;
        private AppSettings? _appSettings;


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
                if (_viewModel != null)
                {
                    _viewModel.SelectedItem = item;
                }

                // 選択されたアイテムまでスクロール
                ScrollToSelectedItem();
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


            try
            {
                _viewModel.Expand(path);
                // 選択されたアイテムまでスクロール
                ScrollToSelectedItem();
            }
            finally
            {

            }
        }

        private void ScrollToSelectedItem()
        {
            if (_viewModel?.SelectedItem == null)
                return;

            var treeView = FindVisualChild<TreeView>(this);
            if (treeView == null)
                return;

            // 選択されたアイテムの親階層を取得
            var path = _viewModel.SelectedItem.FullPath;
            var rootPath = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(rootPath))
                return;

            // ルートドライブを探す
            var rootItem = _viewModel.RootItems.FirstOrDefault(
                item => item.FullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase));
            if (rootItem == null)
                return;

            // ルートから順番にTreeViewItemを取得して展開
            var currentContainer = treeView.ItemContainerGenerator.ContainerFromItem(rootItem) as TreeViewItem;
            if (currentContainer == null)
                return;

            // ルートアイテムをBringIntoViewして、TreeViewItemが生成されるのを待つ
            currentContainer.BringIntoView();
            currentContainer.UpdateLayout();

            // ルートから目的のアイテムまでのパスを分解
            var pathParts = path.Substring(rootPath.Length)
                .Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            var currentPath = rootPath;
            foreach (var part in pathParts)
            {
                currentPath = Path.Combine(currentPath, part);

                // 現在のTreeViewItemの子アイテムを探す
                var nextItem = currentContainer.ItemContainerGenerator.Items
                    .OfType<FileSystemItemModel>()
                    .FirstOrDefault(item => item.FullPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase));

                if (nextItem == null)
                    break;

                // 子アイテムのTreeViewItemを取得
                currentContainer.IsExpanded = true;
                currentContainer.UpdateLayout();

                var nextContainer = currentContainer.ItemContainerGenerator.ContainerFromItem(nextItem) as TreeViewItem;
                if (nextContainer == null)
                    break;

                // 次の階層に進む
                currentContainer = nextContainer;
                currentContainer.BringIntoView();
                currentContainer.UpdateLayout();
            }

            // 最終的な目的のアイテムまでスクロール
            currentContainer.BringIntoView();
            currentContainer.IsSelected = true;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T t)
                    return t;

                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null!;
        }

        private void TreeView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var treeView = sender as TreeView;
            if (treeView == null) return;

            // イベントの発生元を見つける
            if (e.OriginalSource is DependencyObject source)
            {
                var treeViewItem = FindVisualParent<TreeViewItem>(source);
                if (treeViewItem?.ContextMenu != null)
                {
                    // TreeView の DataContext (ViewModel) を取得
                    var viewModel = treeView.DataContext;
                    // TreeViewItem の DataContext (FileSystemItemModel) を取得
                    var item = treeViewItem.DataContext;

                    // コンテキストメニューの DataContext を設定
                    treeViewItem.ContextMenu.DataContext = viewModel;

                    // コマンドパラメータを設定
                    foreach (var menuItem in treeViewItem.ContextMenu.Items.OfType<MenuItem>())
                    {
                        menuItem.CommandParameter = item;
                    }
                }
            }
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null!;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject);
        }
    }
}
