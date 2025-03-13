using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.ViewModels;
using GongSolutions.Wpf.DragDrop;
using Illustra.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Illustra.Events;

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
        private FileOperationHelper _fileOperationHelper;
        private const string FAVORITES_CONTROL_ID = "FavoriteFolders";

        public class CustomDropHandler : IDropTarget
        {
            private readonly FileSystemTreeView _parent = null;

            public CustomDropHandler(FileSystemTreeView parent)
            {
                _parent = parent;
            }

            public void DragOver(IDropInfo dropInfo)
            {
                _parent.TreeView_DragOver(dropInfo);
            }

            public void Drop(IDropInfo dropInfo)
            {
                _parent.TreeView_Drop(dropInfo);
            }
        }

        public FileSystemTreeView()
        {
            InitializeComponent();
            Loaded += FileSystemTreeView_Loaded;
        }

        private void InitializeFileOperationHelper()
        {
            var db = ContainerLocator.Container.Resolve<DatabaseManager>();
            _fileOperationHelper = new FileOperationHelper(db);
        }

        private void FileSystemTreeView_Loaded(object sender, RoutedEventArgs e)
        {
            // ViewModelの初期化とDataContextの設定
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();

            _appSettings = SettingsHelper.GetSettings();
            string? folderPath = null;

            // 起動時フォルダ設定に応じてパスを設定
            switch (_appSettings.StartupMode)
            {
                case AppSettings.StartupFolderMode.LastOpened:
                    folderPath = _appSettings.LastFolderPath;
                    break;
                case AppSettings.StartupFolderMode.Specified:
                    folderPath = _appSettings.StartupFolderPath;
                    break;
                case AppSettings.StartupFolderMode.None:
                default:
                    folderPath = null;
                    break;
            }

            // パスの存在確認
            if (!string.IsNullOrEmpty(folderPath) && !Directory.Exists(folderPath))
            {
                folderPath = null;
            }
            _viewModel = new FileSystemTreeViewModel(_eventAggregator, folderPath);
            DataContext = _viewModel;
            FolderTreeView.ItemsSource = _viewModel.RootItems; // バインド順を DataContext → ItemsSource にする必要あり

            GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(FolderTreeView, new CustomDropHandler(this));

            // 設定の更新を監視するためにお気に入り関連イベントを購読
            _eventAggregator.GetEvent<AddToFavoritesEvent>().Subscribe(path =>
            {
                _appSettings = SettingsHelper.GetSettings();
            });
            _eventAggregator.GetEvent<RemoveFromFavoritesEvent>().Subscribe(path =>
            {
                _appSettings = SettingsHelper.GetSettings();
            });

            // ツリーアイテムを画面内に表示するイベントを購読
            _eventAggregator.GetEvent<BringTreeItemIntoViewEvent>().Subscribe(path =>
            {
                ScrollToSelectedItem();
            });

            // FileOperationHelperの初期化
            InitializeFileOperationHelper();
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

                // ノードをクリックしたときにはスクロールは不要
                // 外部からフォルダが指定されたときは別途スクロール処理を実行している

                // 選択されたアイテムのTreeViewItemを取得して更新を強制
                var treeView = sender as TreeView;
                if (treeView != null)
                {
                    var container = treeView.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                    if (container != null)
                    {
                        container.Foreground = new SolidColorBrush(Colors.Black); // テキストカラーを設定
                        container.Visibility = Visibility.Visible; // 表示状態を設定
                        container.UpdateLayout(); // レイアウトを更新
                    }
                }
            }
        }

        /// <summary>
        /// ビジュアルツリーから指定された型の子要素を検索します
        /// </summary>
        private static T FindDirectVisualChild<T>(DependencyObject obj, string name = null) where T : FrameworkElement
        {
            DependencyObject child = null;
            var childrenCount = VisualTreeHelper.GetChildrenCount(obj);

            for (int i = 0; i < childrenCount; i++)
            {
                child = VisualTreeHelper.GetChild(obj, i);

                if (child != null)
                {
                    if (child is T element)
                    {
                        if (name == null || element.Name == name)
                            return element;
                    }
                    else if (!(child is ItemsPresenter)) // ItemsPresenterをスキップ（子フォルダの検索を防ぐ）
                    {
                        var result = FindDirectVisualChild<T>(child, name);
                        if (result != null)
                            return result;
                    }
                }
            }

            return null;
        }
        private void ScrollToSelectedItem()
        {
            if (_viewModel?.SelectedItem == null)
                return;

            var treeView = FindDirectVisualChild<TreeView>(this);
            if (treeView == null)
                return;

            // 選択されたアイテムに対応するTreeViewItemを直接探す
            var targetItem = GetTreeViewItemForItem(treeView, _viewModel.SelectedItem);
            if (targetItem == null)
                return;

            // ScrollViewerを取得
            ScrollViewer scrollViewer = FindScrollViewer(treeView);
            if (scrollViewer == null)
                return;

            // アイテムの位置を取得
            Point relativePosition = targetItem.TranslatePoint(new Point(0, 0), scrollViewer);

            // アイテムが表示範囲内にあるか確認
            bool isVisible = relativePosition.Y >= 0 &&
                           relativePosition.Y + targetItem.ActualHeight <= scrollViewer.ViewportHeight;

            // 表示範囲外の場合のみスクロール
            if (!isVisible)
            {
                // スクロールして選択アイテムを表示
                targetItem.BringIntoView();
            }
        }

        // TreeView内のScrollViewerを見つける補助メソッド
        private ScrollViewer FindScrollViewer(DependencyObject depObj)
        {
            if (depObj == null)
                return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);

                if (child is ScrollViewer scrollViewer)
                    return scrollViewer;

                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
            }

            return null;
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
                if (treeViewItem != null)
                {
                    // TreeView の DataContext (ViewModel) を取得
                    var viewModel = treeView.DataContext;
                    // TreeViewItem の DataContext (FileSystemItemModel) を取得
                    var item = treeViewItem.DataContext as FileSystemItemModel;

                    if (item != null)
                    {
                        // お気に入り追加メニューの有効/無効を制御
                        var addToFavoritesMenuItem = treeView.ContextMenu?.Items.OfType<MenuItem>()
                            .FirstOrDefault(x => x.Name == "AddToFavoritesMenuItem");

                        if (addToFavoritesMenuItem != null)
                        {
                            addToFavoritesMenuItem.IsEnabled = !IsFavorite(item.FullPath);
                            addToFavoritesMenuItem.CommandParameter = item;
                        }
                    }
                }
            }
        }

        private bool IsFavorite(string path)
        {
            if (_appSettings?.FavoriteFolders == null) return false;
            return _appSettings.FavoriteFolders.Contains(path);
        }

        private void AddToFavorites_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is FileSystemItemModel item)
            {
                _eventAggregator?.GetEvent<AddToFavoritesEvent>()?.Publish(item.FullPath);
            }
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject); // 再帰的に親を検索
        }


        // 作成したすべてのAdornerを追跡
        private List<TreeViewItemHighlightAdorner> _allAdorners = new List<TreeViewItemHighlightAdorner>();
        private TreeViewItem _currentHighlightedItem = null;

        private void TreeViewItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is TreeViewItem item)
            {
                // ヘッダー部分を見つける
                var header = item.Template.FindName("PART_Header", item) as FrameworkElement;

                if (header != null)
                {
                    // マウス位置がヘッダー上にあるか確認
                    Point mousePos = e.GetPosition(header);
                    bool isOverHeader = mousePos.X >= 0 && mousePos.Y >= 0 &&
                                       mousePos.X < header.ActualWidth &&
                                       mousePos.Y < header.ActualHeight;

                    if (isOverHeader)
                    {
                        // 先に全てのAdornerを削除
                        RemoveAllAdorners();

                        // 新しいAdornerを追加
                        var adornerLayer = AdornerLayer.GetAdornerLayer(item);
                        if (adornerLayer != null)
                        {
                            var adorner = new TreeViewItemHighlightAdorner(item);
                            adornerLayer.Add(adorner);
                            _allAdorners.Add(adorner);
                            _currentHighlightedItem = item;
                        }

                        e.Handled = true;
                    }
                }
            }
        }

        private void RemoveAllAdorners()
        {
            // 全てのAdornerを削除
            foreach (var adorner in _allAdorners)
            {
                var layer = AdornerLayer.GetAdornerLayer(adorner.AdornedElement);
                if (layer != null)
                {
                    layer.Remove(adorner);
                }
            }

            _allAdorners.Clear();
            _currentHighlightedItem = null;
        }

        private void TreeViewItem_MouseLeave(object sender, MouseEventArgs e)
        {
            RemoveAllAdorners();
        }

        /// <summary>
        /// ドロップ時の処理
        /// </summary>
        private void TreeView_DragOver(IDropInfo dropInfo)
        {
            var files = DragDropHelper.GetDroppedFiles(dropInfo);
            if (files.Count == 0)
            {
                dropInfo.Effects = DragDropEffects.None;
                return;
            }

            // ドロップ先を取得
            var targetModel = dropInfo.TargetItem as FileSystemItemModel;
            if (targetModel == null || !targetModel.IsFolder)
            {
                dropInfo.Effects = DragDropEffects.None;
                return;
            }

            if (DragDropHelper.IsSameDirectory(files, targetModel.FullPath))
            {
                dropInfo.Effects = DragDropEffects.None;
                return;
            }
            else
            {
                dropInfo.Effects = (dropInfo.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey
                    ? DragDropEffects.Copy
                    : DragDropEffects.Move;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
            }
        }

        private async void TreeView_Drop(IDropInfo dropInfo)
        {
            TreeViewItem targetItem = null;

            try
            {
                var files = DragDropHelper.GetDroppedFiles(dropInfo);
                if (files.Count == 0)
                {
                    return;
                }

                // ドロップ先を取得
                var targetModel = dropInfo.TargetItem as FileSystemItemModel;
                if (targetModel == null || !targetModel.IsFolder)
                {
                    return;
                }

                if (DragDropHelper.IsSameDirectory(files, targetModel.FullPath))
                {
                    // 同じフォルダへのドロップは禁止
                    return;
                }

                // ドロップ先のフォルダパスを取得
                string targetFolderPath = targetModel.FullPath;

                // ドロップ時の選択を防ぐ
                dropInfo.Effects = DragDropEffects.None;
                // dropInfo.Handled = true;

                // コピーまたは移動操作を実行
                bool isCopy = ((dropInfo.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey);
                await _fileOperationHelper.ExecuteFileOperation(files.ToList(), targetFolderPath, isCopy);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Drop処理中にエラーが発生しました: {ex.Message}");
                MessageBox.Show($"ファイル操作中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                // dropInfo.Handled = true;
            }
            finally
            {
                // ドロップ先のアイテムの表示を更新
                if (targetItem != null)
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        var model = targetItem.DataContext as FileSystemItemModel;
                        if (model?.IsExpanded == true)
                        {
                            // フォルダの内容のみを更新
                            model.IsExpanded = false;
                            await Task.Delay(50);
                            model.IsExpanded = true;
                        }
                        return Task.CompletedTask;
                    }, DispatcherPriority.Normal);
                }
            }
        }
    }
}
