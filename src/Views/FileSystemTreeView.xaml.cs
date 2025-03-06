using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
        private FileOperationHelper _fileOperationHelper;
        private TreeViewItem? _lastHighlightedItem; // 最後にハイライトしたアイテム


        public FileSystemTreeView()
        {
            InitializeComponent();
            Loaded += FileSystemTreeView_Loaded;

            // マウスイベントハンドラを追加
            AddHandler(TreeViewItem.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown), true);
        }

        private void InitializeFileOperationHelper()
        {
            var db = ContainerLocator.Container.Resolve<DatabaseManager>();
            _fileOperationHelper = new FileOperationHelper(db);
        }
        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                // ドラッグ中はマウスイベントをキャンセル
                e.Handled = true;
            }
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

                // 選択されたアイテムまでスクロール
                ScrollToSelectedItem();

                // ドラッグ＆ドロップ中のハイライトをクリア
                if (_lastHighlightedItem != null)
                {
                    ResetHighlight(_lastHighlightedItem);
                    _lastHighlightedItem = null;
                }

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
        private async void ScrollToSelectedItem()
        {
            if (_viewModel?.SelectedItem == null)
                return;

            var treeView = FindDirectVisualChild<TreeView>(this);
            if (treeView == null)
                return;

            // 選択されたアイテムのパスを取得
            var path = _viewModel.SelectedItem.FullPath;
            var rootPath = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(rootPath))
                return;

            // ルートドライブを探す
            var rootItem = _viewModel.RootItems.FirstOrDefault(
                item => item.FullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase));
            if (rootItem == null)
                return;

            // ルートから目的のアイテムまでのパスを分解
            var pathParts = path.Substring(rootPath.Length)
                .Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            var currentPath = rootPath;
            var currentContainer = treeView.ItemContainerGenerator.ContainerFromItem(rootItem) as TreeViewItem;
            if (currentContainer == null)
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                currentContainer = treeView.ItemContainerGenerator.ContainerFromItem(rootItem) as TreeViewItem;
                if (currentContainer == null) return;
            }

            // 各階層を展開しながら目的のアイテムまで移動
            foreach (var part in pathParts)
            {
                currentPath = Path.Combine(currentPath, part);
                currentContainer.IsExpanded = true;

                // UIの更新を待つ
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                var nextItem = currentContainer.Items.OfType<FileSystemItemModel>()
                    .FirstOrDefault(item => item.FullPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase));

                if (nextItem == null)
                    break;

                var nextContainer = currentContainer.ItemContainerGenerator.ContainerFromItem(nextItem) as TreeViewItem;
                if (nextContainer == null)
                {
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    nextContainer = currentContainer.ItemContainerGenerator.ContainerFromItem(nextItem) as TreeViewItem;
                    if (nextContainer == null) break;
                }

                currentContainer = nextContainer;
            }

            // 最終的な目的のアイテムまでスクロール
            if (currentContainer != null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    currentContainer.BringIntoView();
                    currentContainer.IsSelected = true;
                }, DispatcherPriority.Render);
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

        /// <summary>
        /// ドラッグオーバー時の処理
        /// </summary>
        private void TreeView_DragOver(object sender, DragEventArgs e)
        {
            try
            {
                // 前回ハイライトしたアイテムを元に戻す
                if (_lastHighlightedItem != null)
                {
                    ResetHighlight(_lastHighlightedItem);
                    _lastHighlightedItem = null;
                }

                // ドラッグデータがファイルかどうかを確認
                if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                // ドロップ先のTreeViewItemを取得
                var targetItem = GetDropTargetItem(e.OriginalSource);
                if (targetItem == null || !(targetItem.DataContext is FileSystemItemModel targetModel) || !targetModel.IsFolder)
                {
                    // フォルダでない場合はドロップ不可
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                // ドラッグ元のパスを取得
                string[] sourceFiles = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (sourceFiles != null && sourceFiles.Length > 0)
                {
                    // ドラッグ元とドロップ先が同じフォルダの場合はドロップ不可
                    foreach (var file in sourceFiles)
                    {
                        string parentDir = Path.GetDirectoryName(file);
                        if (string.Equals(parentDir, targetModel.FullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            // 同じフォルダへのドロップは禁止
                            e.Effects = DragDropEffects.None;
                            e.Handled = true;
                            return;
                        }
                    }
                }

                // Ctrlキーが押されている場合はコピー、それ以外は移動
                if ((e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey)
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.Move;
                }

                // ドロップ先のTreeViewItemをハイライト
                HighlightDropTarget(targetItem);
                _lastHighlightedItem = targetItem;

                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DragOver処理中にエラーが発生しました: {ex.Message}");
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        /// <summary>
        /// ドラッグ操作が終了したときの処理
        /// </summary>
        protected override void OnDragLeave(DragEventArgs e)
        {
            base.OnDragLeave(e);

            // ハイライトを元に戻す
            if (_lastHighlightedItem != null)
            {
                ResetHighlight(_lastHighlightedItem);
                _lastHighlightedItem = null;
            }
        }

        /// <summary>
        /// ドロップ時の処理
        /// </summary>
        private bool _isDragging = false;

        private async void TreeView_Drop(object sender, DragEventArgs e)
        {
            _isDragging = true;
            TreeViewItem targetItem = null;

            try
            {
                // ドラッグデータがファイルかどうかを確認
                if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Handled = true;
                    return;
                }

                // ドロップされたファイルのパスを取得
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0)
                {
                    e.Handled = true;
                    return;
                }

                // ドロップ先のTreeViewItemを取得
                targetItem = GetDropTargetItem(e.OriginalSource);
                if (targetItem == null || !(targetItem.DataContext is FileSystemItemModel targetModel) || !targetModel.IsFolder)
                {
                    e.Handled = true;
                    return;
                }

                // ドラッグ元とドロップ先が同じフォルダの場合はドロップ不可
                foreach (var file in files)
                {
                    string parentDir = Path.GetDirectoryName(file);
                    if (string.Equals(parentDir, targetModel.FullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        // 同じフォルダへのドロップは禁止
                        e.Handled = true;
                        return;
                    }
                }

                // ハイライトを元に戻す
                if (_lastHighlightedItem != null)
                {
                    ResetHighlight(_lastHighlightedItem);
                    _lastHighlightedItem = null;
                }

                // ドロップ先のフォルダパスを取得
                string targetFolderPath = targetModel.FullPath;

                // ドロップ時の選択を防ぐ
                e.Effects = DragDropEffects.None;
                e.Handled = true;

                // コピーまたは移動操作を実行
                bool isCopy = ((e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey);
                await _fileOperationHelper.ExecuteFileOperation(files.ToList(), targetFolderPath, isCopy);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Drop処理中にエラーが発生しました: {ex.Message}");
                MessageBox.Show($"ファイル操作中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            }
            finally
            {
                // ドラッグ状態をリセット
                _isDragging = false;

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

        /// <summary>
        /// ドロップ先のTreeViewItemを取得
        /// </summary>
        private TreeViewItem GetDropTargetItem(object originalSource)
        {
            if (originalSource is DependencyObject depObj)
            {
                return FindVisualParent<TreeViewItem>(depObj);
            }
            return null;
        }

        /// <summary>
        /// ドロップ先のTreeViewItemをハイライト
        /// </summary>
        private void HighlightDropTarget(TreeViewItem item)
        {
            var border = FindDirectVisualChild<Border>(item, "DropTargetBorder");
            if (border != null)
            {
                if (item?.IsSelected == true)
                {
                    // 選択中のアイテムは青みがかった緑色でハイライト
                    border.Background = new SolidColorBrush(Color.FromArgb(80, 0, 180, 180));
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 180, 180));
                }
                else
                {
                    // 非選択アイテムは通常の緑色でハイライト
                    border.Background = new SolidColorBrush(Color.FromArgb(80, 0, 200, 0));
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 200, 0));
                }
                border.BorderThickness = new Thickness(1);
            }
        }

        /// <summary>
        /// ハイライトを元に戻す
        /// </summary>
        private void ResetHighlight(TreeViewItem item)
        {
            if (item == null) return;

            var border = FindDirectVisualChild<Border>(item, "DropTargetBorder");
            if (border != null)
            {
                // ハイライトとボーダーをクリア
                border.Background = Brushes.Transparent;
                border.BorderBrush = Brushes.Transparent;
                border.BorderThickness = new Thickness(0);
            }
        }
    }
}
