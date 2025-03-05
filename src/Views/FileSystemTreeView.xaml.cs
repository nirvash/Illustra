using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

        /// <summary>
        /// ビジュアルツリーから指定された型の子要素を検索します
        /// </summary>
        private static T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);

                if (child != null && child is T)
                    return (T)child;

                T childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }

            return null;
        }
        private void ScrollToSelectedItem()
        {
            if (_viewModel?.SelectedItem == null)
                return;

            var treeView = FindVisualChild<TreeView>(this);
            if (treeView == null)
                return;

            // 選択されたアイテムが既に表示されているかをチェック
            if (TreeViewVisibilityHelper.IsItemVisible(treeView, _viewModel.SelectedItem))
                return;

            // 以下、アイテムが表示されていない場合の処理
            Debug.WriteLine($"Scrolling to item: {_viewModel.SelectedItem.Name}");

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
                var nextItem = _viewModel.RootItems.FirstOrDefault(item =>
                            item.FullPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase));

                if (nextItem == null)
                {
                    // 子階層から探す
                    nextItem = currentContainer.Items.OfType<FileSystemItemModel>()
                        .FirstOrDefault(item => item.FullPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase));

                    if (nextItem == null)
                        break;
                }

                // 子アイテムのTreeViewItemを取得
                currentContainer.IsExpanded = true;
                currentContainer.UpdateLayout();

                var nextContainer = currentContainer.ItemContainerGenerator.ContainerFromItem(nextItem) as TreeViewItem;
                if (nextContainer == null)
                    break;

                // 次の階層に進む
                currentContainer = nextContainer;
            }

            // 最終的な目的のアイテムまでスクロール
            if (currentContainer != null)
            {
                currentContainer.BringIntoView();
                currentContainer.IsSelected = true;
            }
        }



        private TreeViewItem GetTreeViewItemForItem(ItemsControl parent, object item)
        {
            if (parent == null || item == null)
                return null;

            if (parent.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
                return null;

            var container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            if (container != null)
                return container;

            // 子アイテムを再帰的に検索
            for (int i = 0; i < parent.Items.Count; i++)
            {
                var childContainer = parent.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (childContainer == null)
                    continue;

                if (childContainer.IsExpanded)
                {
                    var result = GetTreeViewItemForItem(childContainer, item);
                    if (result != null)
                        return result;
                }
            }

            return null;
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

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject); // 再帰的に親を検索
        }
    }
}
