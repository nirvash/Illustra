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
        private AppSettingsModel? _appSettings;
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

            // 初期状態ではフォルダを選択しない（App.xaml.csからの選択を待つ）
            _viewModel = new FileSystemTreeViewModel(_eventAggregator, null);
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

            _viewModel = new FileSystemTreeViewModel(_eventAggregator, null);
            DataContext = _viewModel;
            FolderTreeView.ItemsSource = _viewModel.RootItems; // バインド順を DataContext → ItemsSource にする必要あり

            GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(FolderTreeView, new CustomDropHandler(this));


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
            // スクロールして選択アイテムを表示
            Application.Current.Dispatcher.InvokeAsync(async () =>
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

                // コンテナ生成を非同期で待つ
                await WaitForTreeViewItemToBeReady(targetItem);

                // 以下のロジックは仮想化された TreeView には対応していない
                // ItemsPresenter / ItemsHost を取得
                ItemsPresenter itemsPresenter = UIHelper.FindVisualChild<ItemsPresenter>(treeView);
                Panel itemsHost = VisualTreeHelper.GetChild(itemsPresenter, 0) as Panel;

                // ItemsHost に対する TreeViewItem の位置
                Point positionInHost = targetItem.TranslatePoint(new Point(0, 0), itemsHost);

                // ScrollViewer を取得
                ScrollViewer scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(treeView);

                // 見えている範囲を計算
                double itemTopInHost = positionInHost.Y;
                double itemBottomInHost = itemTopInHost + targetItem.ActualHeight;

                // 現在のスクロール範囲
                double viewportHeight = scrollViewer.ViewportHeight;
                double verticalOffset = scrollViewer.VerticalOffset;
                double viewportBottom = verticalOffset + viewportHeight;

                // スクロール必要なら実行
                if (itemTopInHost < verticalOffset || itemBottomInHost > viewportBottom)
                {
                    // スクロールして選択アイテムを表示
                    // targetItem.BringIntoView();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    scrollViewer.ScrollToVerticalOffset(itemTopInHost - 20); // Margin 20
                }

                // --- 水平方向のスクロール処理を追加 ---

                // 見えている範囲を計算 (水平)
                double itemLeftInHost = positionInHost.X;
                // TreeViewItem の幅を取得 (ヘッダー部分などを考慮する必要があるかもしれない)
                // シンプルに ActualWidth を使う
                double itemRightInHost = itemLeftInHost + targetItem.ActualWidth;

                // 現在のスクロール範囲 (水平)
                double viewportWidth = scrollViewer.ViewportWidth;
                double horizontalOffset = scrollViewer.HorizontalOffset;
                double viewportRight = horizontalOffset + viewportWidth;
                const double horizontalMargin = 20; // 水平方向のマージン

                // スクロール必要なら実行 (水平)
                // アイテムの右端 + マージン がビューポートの右端より外側にある場合
                if (itemRightInHost + horizontalMargin > viewportRight)
                {
                    // アイテムの右端 + マージン が見えるようにスクロール
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    scrollViewer.ScrollToHorizontalOffset(itemRightInHost + horizontalMargin - viewportWidth);
                }
                // アイテムの左端がビューポートの左端より外側にある場合 (左スクロール)
                else if (itemLeftInHost < horizontalOffset)
                {
                    // アイテムの左端が見えるようにスクロール
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    scrollViewer.ScrollToHorizontalOffset(itemLeftInHost);
                }

            }, DispatcherPriority.Background);
        }

        private async Task WaitForTreeViewItemToBeReady(TreeViewItem item)
        {
            // 視覚ツリー上に乗るまで待つ
            while (!item.IsVisible)
            {
                await Task.Delay(50); // 少し待つ（調整可）
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }

            // レイアウトが落ち着くのも念のため待つ
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Task.Delay(50); // 少し待つ（調整可）
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
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
