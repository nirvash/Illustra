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
using Illustra.Helpers; // DialogHelper を使うために追加
using System; // IProgress を使うために追加

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
        private bool _isScrollingProgrammatically = false; // カスタムスクロール実行中フラグ
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
                // ScrollToSelectedItem(); // Loaded 時の不要なスクロールを削除。選択変更時に実行される。
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

                // --- ノードをクリックしたときにもスクロールを実行 ---
                ScrollToSelectedItem();

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

                // ScrollViewer を取得
                ScrollViewer scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(treeView);
                if (scrollViewer == null) return; // ScrollViewer がなければ処理中断

                // --- TreeViewItem のヘッダー部分を取得 ---
                var headerElement = targetItem.Template?.FindName("PART_Header", targetItem) as FrameworkElement;
                if (headerElement == null) return; // ヘッダーがなければスクロール計算不可

                // --- アイテムのサイズと位置を取得 ---
                double itemHeight = headerElement.DesiredSize.Height;
                double itemWidth = headerElement.DesiredSize.Width;

                // ItemsHost を取得 (仮想化非対応前提)
                ItemsPresenter itemsPresenter = UIHelper.FindVisualChild<ItemsPresenter>(treeView);
                Panel itemsHost = VisualTreeHelper.GetChild(itemsPresenter, 0) as Panel;

                // ItemsHost に対する TreeViewItem の位置 (垂直スクロール用)
                Point positionInHost = targetItem.TranslatePoint(new Point(0, 0), itemsHost);
                double itemTopInHost = positionInHost.Y;
                double itemBottomInHost = itemTopInHost + itemHeight;

                // ScrollViewer 内でのヘッダーの位置 (水平スクロール用)
                Point positionInScrollViewer = headerElement.TranslatePoint(new Point(0, 0), scrollViewer);
                double itemLeftInScrollViewer = positionInScrollViewer.X;
                double itemRightInScrollViewer = itemLeftInScrollViewer + itemWidth;

                // --- 現在のスクロール状態を取得 ---
                double viewportHeight = scrollViewer.ViewportHeight;
                double viewportWidth = scrollViewer.ViewportWidth;
                double currentVerticalOffset = scrollViewer.VerticalOffset;
                double currentHorizontalOffset = scrollViewer.HorizontalOffset;
                double viewportBottom = currentVerticalOffset + viewportHeight;

                // --- 目標オフセットを計算 ---
                double targetVerticalOffset = currentVerticalOffset; // 初期値は現在のオフセット
                double targetHorizontalOffset = currentHorizontalOffset; // 初期値は現在のオフセット
                const double horizontalMargin = 20.0;

                // --- 垂直方向の目標オフセット計算 ---
                bool needsVerticalScroll = itemTopInHost < currentVerticalOffset || itemBottomInHost > viewportBottom;
                if (needsVerticalScroll)
                {
                    targetVerticalOffset = itemTopInHost + (itemHeight / 2) - (viewportHeight / 2);
                    targetVerticalOffset = Math.Max(0, Math.Min(targetVerticalOffset, scrollViewer.ScrollableHeight));
                }

                // --- 水平方向の目標オフセット計算 ---
                bool isLeftCut = itemLeftInScrollViewer < horizontalMargin;
                bool isRightCut = itemRightInScrollViewer > viewportWidth - horizontalMargin;
                bool needsHorizontalScroll = isLeftCut || isRightCut;

                if (needsHorizontalScroll)
                {
                    // ケース1: アイテム幅がビューポート幅より大きい
                    if (itemWidth > viewportWidth)
                    {
                        // 常に左端が見えることを優先
                        targetHorizontalOffset = currentHorizontalOffset + itemLeftInScrollViewer - horizontalMargin;
                    }
                    // ケース2: アイテム幅がビューポート幅以下
                    else
                    {
                        if (isLeftCut) // 左が見切れ
                        {
                            targetHorizontalOffset = currentHorizontalOffset + itemLeftInScrollViewer - horizontalMargin;
                        }
                        else // 右が見切れ (isRightCut is true)
                        {
                            double rightAlignedOffset = currentHorizontalOffset + itemRightInScrollViewer - (viewportWidth - horizontalMargin);
                            double predictedItemLeft = itemLeftInScrollViewer - (rightAlignedOffset - currentHorizontalOffset);

                            if (predictedItemLeft < horizontalMargin)
                            {
                                targetHorizontalOffset = currentHorizontalOffset + itemLeftInScrollViewer - horizontalMargin; // 左基準に戻す
                            }
                            else
                            {
                                targetHorizontalOffset = rightAlignedOffset; // 右基準を採用
                            }
                        }
                    }

                    // スクロール範囲内に収める
                    targetHorizontalOffset = Math.Max(0, Math.Min(targetHorizontalOffset, scrollViewer.ScrollableWidth));
                }

                // --- スクロール実行 ---
                bool verticalChanged = Math.Abs(targetVerticalOffset - currentVerticalOffset) > 1.0;
                bool horizontalChanged = Math.Abs(targetHorizontalOffset - currentHorizontalOffset) > 1.0;

                // 垂直または水平スクロールが必要な場合のみ実行
                if (verticalChanged || horizontalChanged)
                {
                    // 念のため Dispatcher で UI スレッドでの実行を保証し、描画を待つ
                    await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                    // 垂直・水平オフセットを連続して設定
                    if (verticalChanged)
                    {
                        scrollViewer.ScrollToVerticalOffset(targetVerticalOffset);
                    }
                    if (horizontalChanged)
                    {
                        scrollViewer.ScrollToHorizontalOffset(targetHorizontalOffset);
                    }
                }
            } // ラムダ式の閉じ括弧
            , DispatcherPriority.Background); // InvokeAsync の閉じ括弧とセミコロン
        }

        // TreeViewItem の標準 BringIntoView 動作を常に抑制するためのハンドラ
        // XAML の Style 内 EventSetter から呼び出されます
        private void TreeViewItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            // 標準のスクロール動作をキャンセルする
            e.Handled = true;
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

                // --- DialogHelper を使用してファイル操作を実行 ---
                string dialogTitle = isCopy ? (string)FindResource("String_Dialog_FileCopyTitle") : (string)FindResource("String_Dialog_FileMoveTitle");
                var cts = new CancellationTokenSource(); // CancellationTokenSource を生成
                (IProgress<FileOperationProgressInfo> progress, Action closeDialog) = (null, null); // 初期化

                try
                {
                    // 新しい DialogHelper を使用してダイアログを表示 (静的呼び出しに戻す)
                    (progress, closeDialog) =
                        await DialogHelper.ShowProgressDialogAsync(
                            Window.GetWindow(this), // FileSystemTreeView は UserControl なので this でOK
                            dialogTitle,
                            cts); // cts を渡す

                    // ExecuteFileOperation を Task.Run でバックグラウンド実行
                    await Task.Run(async () =>
                    {
                        await _fileOperationHelper.ExecuteFileOperation(files.ToList(), targetFolderPath, isCopy, progress, null, cts.Token); // Pass CancellationToken
                    });
                }
                catch (OperationCanceledException)
                {
                    // Handle cancellation (e.g., log, update UI if needed)
                    System.Diagnostics.Debug.WriteLine("File operation cancelled in FileSystemTreeView.");
                }
                finally
                {
                    // キャンセルされていなければダイアログを閉じる
                    if (cts != null && !cts.IsCancellationRequested)
                    {
                        closeDialog?.Invoke();
                    }
                    cts?.Dispose(); // Dispose CancellationTokenSource
                }
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
