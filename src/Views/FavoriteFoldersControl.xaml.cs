using Illustra.Shared.Models.Tools; // Added for McpOpenFolderEventArgs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using Illustra.Helpers;
using System.IO;
using Illustra.Events;
using System.Diagnostics;
using GongSolutions.Wpf.DragDrop;
using Illustra.Models;
using Illustra.Controls;
using System.Windows.Documents;
using System.Threading.Tasks; // Task を使うために追加
using System; // IProgress を使うために追加
// using Illustra.Helpers; // CS0105 Fix: Redundant using (DialogHelper を使うために追加)
using Illustra.Shared.Models; // Added for MCP events

namespace Illustra.Views
{
    public partial class FavoriteFoldersControl : UserControl, IActiveAware
    {
        private ObservableCollection<FavoriteFolderModel> _favoriteFolders = [];
        private AppSettingsModel _appSettings;
        private IEventAggregator? _eventAggregator;
        // DialogHelper フィールドを削除
        private bool ignoreSelectedChangedOnce;
        private const string CONTROL_ID = "FavoriteFolders";

        // ConvertToModels と ConvertToPaths は不要になったため削除

        #region IActiveAware Implementation
#pragma warning disable 0067 // 使用されていませんという警告を無視
        public bool IsActive { get; set; }
        public event EventHandler? IsActiveChanged;
#pragma warning restore 0067 // 警告の無視を終了
        #endregion

        // OpenInNewTabRequested イベント定義を削除 (EventAggregator を使用するため)
        public class FavoriteFolderDragHandler : DefaultDragHandler
        {
            public override void StartDrag(IDragInfo dragInfo)
            {
                // ドラッグするアイテム数を制限（ここでは単一選択のみ想定）
                if (dragInfo.SourceItems.Cast<object>().Count() > 1)
                {
                    return;
                }

                var item = dragInfo.SourceItem as FavoriteFolderModel;
                if (item != null)
                {
                    // ドラッグするデータを設定
                    dragInfo.Data = item;
                    dragInfo.Effects = DragDropEffects.Move; // Move に設定することでコピー操作を抑制

                    // ドラッグプレビューテンプレートを設定
                    if (dragInfo.VisualSource is FrameworkElement sourceElement)
                    {
                        var template = sourceElement.TryFindResource("FavoriteFolderDragAdornerTemplate") as DataTemplate;
                        // XAML側で設定するため、ここでの設定は不要
                        // if (template != null)
                        // {
                        //     // dragInfo.DragAdornerTemplate = template; // CS1061 エラーのため削除
                        // }
                    }
                }
                else
                {
                    // FavoriteFolderModel 以外はデフォルトの処理
                    base.StartDrag(dragInfo);
                }
            }
        }


        public class CustomDropHandler : DefaultDropHandler
        {
            private readonly FavoriteFoldersControl _parent = null;

            public CustomDropHandler(FavoriteFoldersControl parent)
            {
                _parent = parent;
            }

            public override void DragOver(IDropInfo e)
            {
                // base.DragOver(e); // DefaultDropHandler の DragOver は不要な場合があるためコメントアウトまたは削除検討
                try
                {
                    // --- お気に入りフォルダの並び替え処理 ---
                    if (e.Data is FavoriteFolderModel)
                    {
                        // ドロップ先がアイテム自体の上か、アイテムの間かを判定
                        bool isOverItem = e.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter);

                        if (isOverItem)
                        {
                            // アイテム上へのドロップは許可しない (移動のみ)
                            e.Effects = DragDropEffects.Move;
                            e.DropTargetAdorner = null; // アドーナーも表示しない
                        }
                        else
                        {
                            // アイテム間へのドロップ（挿入）は許可
                            e.Effects = DragDropEffects.Move;
                            e.DropTargetAdorner = DropTargetAdorners.Insert; // 挿入アドーナーを表示
                        }
                        // 並び替え中は通常のドラッグオーバーハイライトを解除
                        if (_parent._dragOverHighlightedItem != null)
                        {
                             _parent.ClearHighlight(_parent._dragOverHighlightedItem);
                             _parent._dragOverHighlightedItem = null;
                        }
                        return; // 並び替えの場合は以降の処理は不要
                    }

                    // --- ファイル/フォルダのドロップ処理 ---
                    var files = DragDropHelper.GetDroppedFiles(e);
                    var targetModel = e.TargetItem as FavoriteFolderModel;
                    bool canDrop = false;
                    TreeViewItem? targetItem = null; // ハイライト対象の TreeViewItem

                    if (files.Count > 0 && targetModel?.Path != null && Directory.Exists(targetModel.Path) && !DragDropHelper.IsSameDirectory(files, targetModel.Path))
                    {
                        e.Effects = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey
                            ? DragDropEffects.Copy
                            : DragDropEffects.Move;
                        canDrop = true;
                        // TargetItem から TreeViewItem を取得
                        targetItem = _parent.GetTreeViewItemForItem(_parent.FavoriteFoldersTreeView, targetModel);
                    }
                    else
                    {
                        e.Effects = DragDropEffects.None;
                    }

                    // ハイライト処理 (FileSystemTreeView と同様)
                    TreeViewItem? newHighlightTarget = null;
                    if (canDrop && targetItem != null)
                    {
                        newHighlightTarget = targetItem;
                    }

                    // ハイライト状態の更新
                    if (_parent._dragOverHighlightedItem != newHighlightTarget)
                    {
                        _parent.ClearHighlight(_parent._dragOverHighlightedItem); // 以前のハイライトを解除
                        _parent.SetHighlight(newHighlightTarget);         // 新しいターゲットをハイライト (null なら解除される)
                        _parent._dragOverHighlightedItem = newHighlightTarget;
                    }

                    // デフォルトの Adorner は使用しない
                    e.DropTargetAdorner = null;
                }
                catch (Exception ex) // エラーハンドリングを改善
                {
                    Debug.WriteLine($"DragOver処理中にエラーが発生しました: {ex.Message}");
                    e.Effects = DragDropEffects.None;
                    // ハイライトも解除
                    if (_parent._dragOverHighlightedItem != null)
                    {
                         _parent.ClearHighlight(_parent._dragOverHighlightedItem);
                         _parent._dragOverHighlightedItem = null;
                    }
                }
            }

            public override async void Drop(IDropInfo e)
            {
                try
                {
                    // お気に入りリスト並び替え
                    if (e.Data is FavoriteFolderModel)
                    {
                        var isTreeViewItem = e.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter) && e.VisualTargetItem is TreeViewItem;
                        if (isTreeViewItem)
                        {
                            return;
                        }
                        base.Drop(e);
                        return;
                    }

                    var files = DragDropHelper.GetDroppedFiles(e);
                    if (files.Count == 0) return;

                    // ドロップ先を取得
                    var targetModel = e.TargetItem as FavoriteFolderModel;
                    if (targetModel?.Path == null || !Directory.Exists(targetModel.Path))
                    {
                        e.Effects = DragDropEffects.None;
                        return;
                    }

                    if (DragDropHelper.IsSameDirectory(files, targetModel.Path))
                    {
                        // 同じフォルダへのドロップは禁止
                        e.Effects = DragDropEffects.None;
                        return;
                    }

                    bool isCopy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
                    var db = ContainerLocator.Container.Resolve<DatabaseManager>();
                    var fileOperationHelper = new FileOperationHelper(db);

                    // --- DialogHelper を使用してファイル操作を実行 ---
                    // FindResource と Window.GetWindow は _parent を介して呼び出す
                    string dialogTitle = isCopy ? (string)_parent.FindResource("String_Dialog_FileCopyTitle") : (string)_parent.FindResource("String_Dialog_FileMoveTitle");
                    var cts = new CancellationTokenSource(); // CancellationTokenSource を生成
                    (IProgress<FileOperationProgressInfo> progress, Action closeDialog) = (null, null); // 初期化

                    try
                    {
                        // 新しい DialogHelper を使用してダイアログを表示 (静的呼び出しに戻す)
                        (progress, closeDialog) =
                            await DialogHelper.ShowProgressDialogAsync(
                                Window.GetWindow(_parent), // this の代わりに _parent を渡す
                                dialogTitle,
                                cts); // cts を渡す

                        // ExecuteFileOperation を Task.Run でバックグラウンド実行
                        await Task.Run(async () =>
                        {
                            await fileOperationHelper.ExecuteFileOperation(files.ToList(), targetModel.Path, isCopy, progress, null, cts.Token); // Pass CancellationToken
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        // Handle cancellation (e.g., log, update UI if needed)
                        System.Diagnostics.Debug.WriteLine("File operation cancelled in FavoriteFoldersControl.");
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
                }
                finally // finally ブロックを追加
                {
                    // ドラッグオーバーのハイライトを解除
                    if (_parent._dragOverHighlightedItem != null)
                    {
                        _parent.ClearHighlight(_parent._dragOverHighlightedItem);
                        _parent._dragOverHighlightedItem = null;
                    }
                }
            }
        }

        public FavoriteFoldersControl()
        {
            InitializeComponent();
            Loaded += FavoriteFoldersControl_Loaded;

            // 設定を読み込む
            _appSettings = SettingsHelper.GetSettings();

            // お気に入りフォルダの初期化
            // AppSettingsModel の FavoriteFolders は ObservableCollection<FavoriteFolderModel> になったので直接代入
            _favoriteFolders = _appSettings.FavoriteFolders;
            FavoriteFoldersTreeView.ItemsSource = _favoriteFolders;
            DataContext = this;
        }

        private void FavoriteFoldersControl_Loaded(object sender, RoutedEventArgs e)
        {
            // ContainerLocatorを使ってEventAggregatorを取得
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<McpOpenFolderEvent>().Subscribe(OnMcpFolderSelected, ThreadOption.UIThread, false, // Renamed
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視

            // お気に入り関連イベントの設定
            _eventAggregator.GetEvent<AddToFavoritesEvent>().Subscribe(AddFavoriteFolder);
            _eventAggregator.GetEvent<RemoveFromFavoritesEvent>().Subscribe(HandleRemoveFromFavoritesEvent); // 新しいハンドラを登録

            GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(FavoriteFoldersTreeView, new CustomDropHandler(this));
            GongSolutions.Wpf.DragDrop.DragDrop.SetDragHandler(FavoriteFoldersTreeView, new FavoriteFolderDragHandler()); // カスタムドラッグハンドラを設定

        }

        private void OnMcpFolderSelected(McpOpenFolderEventArgs args) // Renamed and changed args type
        {
            var selectedModel = FavoriteFoldersTreeView.SelectedItem as FavoriteFolderModel;
            if (args.FolderPath == selectedModel?.Path) return; // Changed property name
            if (_favoriteFolders.Any(f => f.Path == args.FolderPath)) // Changed property name
            {
                var targetModel = _favoriteFolders.FirstOrDefault(f => f.Path == args.FolderPath); // Changed property name
                var item = targetModel != null ?
                    FavoriteFoldersTreeView.ItemContainerGenerator.ContainerFromItem(targetModel) as TreeViewItem : null;
                if (item != null)
                {
                    ignoreSelectedChangedOnce = true;
                    item.IsSelected = true;
                }
            }
            else if (FavoriteFoldersTreeView.SelectedItem != null)
            {
                // 選択を解除
                ignoreSelectedChangedOnce = true;
                var selectedItem = FavoriteFoldersTreeView.ItemContainerGenerator.ContainerFromItem(FavoriteFoldersTreeView.SelectedItem) as TreeViewItem;
                if (selectedItem != null)
                {
                    selectedItem.IsSelected = false;
                }
            }
        }

        public void SetCurrentSettings()
        {
            // お気に入りフォルダの設定を保存
            // AppSettingsModel の FavoriteFolders は ObservableCollection<FavoriteFolderModel> になったので直接代入
            _appSettings.FavoriteFolders = _favoriteFolders;
        }

        private void FavoriteFoldersTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (ignoreSelectedChangedOnce)
            {
                ignoreSelectedChangedOnce = false;
                return;
            }

            ignoreSelectedChangedOnce = false;
            if (e.NewValue is FavoriteFolderModel folder && !string.IsNullOrEmpty(folder.Path))
            {
                if (Directory.Exists(folder.Path))
                {
                    // フォルダ選択イベントを発行
                    Debug.WriteLine($"FavoriteFoldersTreeView: SelectedItemChanged: Publish Events: {folder.Path}");
                    _eventAggregator?.GetEvent<McpOpenFolderEvent>().Publish( // Renamed
                        new McpOpenFolderEventArgs // Renamed
                        {
                            FolderPath = folder.Path,
                            SourceId = CONTROL_ID,
                            ResultCompletionSource = null // No need to wait for result from UI interaction
                        });
                    _eventAggregator?.GetEvent<SelectFileRequestEvent>().Publish("");
                }
            }
        }

        public ObservableCollection<FavoriteFolderModel> FavoriteFoldersList
        {
            get => _favoriteFolders;
            set
            {
                _favoriteFolders = value;
                FavoriteFoldersTreeView.ItemsSource = _favoriteFolders;
            }
        }

        /// <summary>
        /// Adds a folder path to the list of favorite folders if it is not already present.
        /// </summary>
        /// <param name="folderPath">The path of the folder to add to the favorites list.</param>
        public void AddFavoriteFolder(string folderPath)
        {
            if (!_favoriteFolders.Any(f => f.Path == folderPath))
            {
                _favoriteFolders.Add(new FavoriteFolderModel(folderPath));
                SetCurrentSettings();
                SettingsHelper.SaveSettings(_appSettings);
            }
        }

        // 引数を FavoriteFolderModel に変更
        public void RemoveFavoriteFolder(FavoriteFolderModel folder)
        {
            if (folder != null && _favoriteFolders.Contains(folder))
            {
                _favoriteFolders.Remove(folder);
                SetCurrentSettings();
                SettingsHelper.SaveSettings(_appSettings);
            }
        }

        // RemoveFromFavoritesEvent を処理するメソッド
        private void HandleRemoveFromFavoritesEvent(string folderPath)
        {
            var folder = _favoriteFolders.FirstOrDefault(f => f.Path == folderPath);
            if (folder != null)
            {
                RemoveFavoriteFolder(folder); // FavoriteFolderModel を引数とするメソッドを呼び出す
            }
        }

        // ハイライト関連のフィールド
        private TreeViewItem? _currentHighlightedItem = null; // 通常のマウスオーバーでハイライトされているアイテム
        private TreeViewItem? _dragOverHighlightedItem = null; // ドラッグオーバーでハイライトされているアイテム
        // private List<TreeViewItemHighlightAdorner> _allAdorners = new List<TreeViewItemHighlightAdorner>(); // Adorner 不要
        // private bool _isContextMenuOpen = false; // ContextMenuOpening/Closing で直接制御するため不要

        // ハイライトを設定するヘルパーメソッド
        private void SetHighlight(TreeViewItem item)
        {
            if (item == null) return;
            // MahApps のテーマブラシを使用 (DynamicResource として取得)
            if (TryFindResource("MahApps.Brushes.Accent3") is Brush backgroundBrush)
            {
                item.Background = backgroundBrush;
            }
            if (TryFindResource("MahApps.Brushes.Selected.Foreground") is Brush foregroundBrush)
            {
                item.Foreground = foregroundBrush;
            }
        }

        // ハイライトを解除するヘルパーメソッド
        private void ClearHighlight(TreeViewItem? item)
        {
            if (item == null) return;
            item.ClearValue(Control.BackgroundProperty);
            item.ClearValue(Control.ForegroundProperty);
        }


        private void TreeViewItem_MouseEnter(object sender, MouseEventArgs e)
        {
            // ContextMenu が開いている間は無視するロジックは不要 (Closing で解除されるため)

            if (sender is TreeViewItem item)
            {
                // マウスがヘッダー部分にあるか確認 (FileSystemTreeView と同じロジック)
                var header = item.Template?.FindName("PART_Header", item) as FrameworkElement;
                if (header != null)
                {
                    Point mousePos = e.GetPosition(header);
                    bool isOverHeader = mousePos.X >= 0 && mousePos.Y >= 0 &&
                                       mousePos.X < header.ActualWidth &&
                                       mousePos.Y < header.ActualHeight;

                    if (isOverHeader)
                    {
                        // 以前のハイライトがあればクリア
                        if (_currentHighlightedItem != null && _currentHighlightedItem != item)
                        {
                            ClearHighlight(_currentHighlightedItem);
                        }

                        // 新しいアイテムをハイライト
                        SetHighlight(item);
                        _currentHighlightedItem = item;
                        e.Handled = true; // 親へのイベント伝播を止める
                    }
                }
            }
        }

        // RemoveAllAdorners は不要

        private void TreeViewItem_MouseLeave(object sender, MouseEventArgs e)
        {
            // ContextMenu が開いている間は無視するロジックは不要

            if (sender is TreeViewItem item && item == _currentHighlightedItem)
            {
                // マウスが本当にアイテムの境界から外れたか確認
                Point mousePos = e.GetPosition(item);
                 if (mousePos.X < 0 || mousePos.Y < 0 || mousePos.X >= item.ActualWidth || mousePos.Y >= item.ActualHeight)
                 {
                    ClearHighlight(item);
                    _currentHighlightedItem = null;
                 }
            }
        }

        // ContextMenuOpening イベントハンドラ (XAML から呼び出す)
        public void TreeViewItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is TreeViewItem item)
            {
                // 右クリックされたアイテムがハイライトされていなければ、既存のハイライトを消す
                if (item != _currentHighlightedItem)
                {
                    ClearHighlight(_currentHighlightedItem);
                    _currentHighlightedItem = null;
                }
                // 右クリックされたアイテムがハイライト対象である場合、
                // MouseLeave で解除されないようにする処理は不要になった
                // (ContextMenuClosing でマウス位置を見て解除するため)
            }
        }

        // ContextMenuClosing イベントハンドラ (XAML から呼び出す)
        public void TreeViewItem_ContextMenuClosing(object sender, ContextMenuEventArgs e)
        {
            if (sender is TreeViewItem item)
            {
                // メニューが閉じた時点でマウスがアイテムの外に出ていたらハイライトを解除
                Point mousePos = Mouse.GetPosition(item);
                bool isMouseTrulyOver = mousePos.X >= 0 && mousePos.Y >= 0 &&
                                        mousePos.X < item.ActualWidth && mousePos.Y < item.ActualHeight;
                if (!isMouseTrulyOver)
                {
                    if (item == _currentHighlightedItem) // 念のため確認
                    {
                        ClearHighlight(item);
                        _currentHighlightedItem = null;
                    }
                }
                // マウスがまだ上にある場合はハイライトは維持される
            }
        }

        private void FavoriteFoldersTreeView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var treeView = sender as TreeView;
            if (treeView?.ContextMenu == null) return;

            // イベントの発生元から TreeViewItem と FavoriteFolderModel を取得
            var treeViewItem = FindVisualParent<TreeViewItem>(e.OriginalSource as DependencyObject);
            var selectedFolder = treeViewItem?.DataContext as FavoriteFolderModel;

            // メニュー項目を取得
            var setDisplayNameMenuItem = treeView.ContextMenu.Items.OfType<MenuItem>().FirstOrDefault(x => x.Name == "SetDisplayNameMenuItem");
            var removeDisplayNameMenuItem = treeView.ContextMenu.Items.OfType<MenuItem>().FirstOrDefault(x => x.Name == "RemoveDisplayNameMenuItem");
            var openInNewTabMenuItem = treeView.ContextMenu.Items.OfType<MenuItem>().FirstOrDefault(x => x.Name == "OpenInNewTabMenuItem");
            var removeFavoriteMenuItem = treeView.ContextMenu.Items.OfType<MenuItem>().FirstOrDefault(x => x.Name == "RemoveFromFavoritesMenuItem");
            var openInExplorerMenuItem = treeView.ContextMenu.Items.OfType<MenuItem>().FirstOrDefault(x => x.Name == "OpenInExplorerMenuItem");

            // 選択されたアイテムがない場合、すべてのカスタムメニューを無効化
            if (selectedFolder == null || string.IsNullOrEmpty(selectedFolder.Path))
            {
                if (setDisplayNameMenuItem != null) setDisplayNameMenuItem.IsEnabled = false;
                if (removeDisplayNameMenuItem != null) removeDisplayNameMenuItem.IsEnabled = false;
                if (openInNewTabMenuItem != null) openInNewTabMenuItem.IsEnabled = false;
                if (removeFavoriteMenuItem != null) removeFavoriteMenuItem.IsEnabled = false;
                if (openInExplorerMenuItem != null) openInExplorerMenuItem.IsEnabled = false;
                return;
            }

            // 各メニュー項目の CommandParameter と IsEnabled を設定
            if (setDisplayNameMenuItem != null)
            {
                setDisplayNameMenuItem.CommandParameter = selectedFolder;
                setDisplayNameMenuItem.IsEnabled = true;
            }
            if (removeDisplayNameMenuItem != null)
            {
                removeDisplayNameMenuItem.CommandParameter = selectedFolder;
                // DisplayName が設定されている場合のみ有効
                removeDisplayNameMenuItem.IsEnabled = selectedFolder.HasDisplayName;
            }
            if (openInNewTabMenuItem != null) // 追加
            {
                openInNewTabMenuItem.CommandParameter = selectedFolder;
                openInNewTabMenuItem.IsEnabled = true;
            }
            if (removeFavoriteMenuItem != null)
            {
                removeFavoriteMenuItem.CommandParameter = selectedFolder;
                removeFavoriteMenuItem.IsEnabled = true;
            }
            if (openInExplorerMenuItem != null)
            {
                openInExplorerMenuItem.CommandParameter = selectedFolder;
                openInExplorerMenuItem.IsEnabled = true;
            }
        }

        // 表示名を設定するメニューのクリックイベントハンドラ
        private void SetDisplayName_Click(object sender, RoutedEventArgs e) // CS1998 Fix: Removed unnecessary async
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is FavoriteFolderModel folder)
            {
                var dialog = new SetDisplayNameDialog(folder)
                {
                    Owner = Window.GetWindow(this) // ダイアログの親ウィンドウを設定
                };

                // MetroWindow.ShowDialog() は bool? を返すため、true と比較する
                if (dialog.ShowDialog() == true)
                {
                    folder.DisplayName = dialog.ResultDisplayName; // ダイアログで設定された表示名を取得 (プロパティ名を変更)
                    SetCurrentSettings();
                    SettingsHelper.SaveSettings(_appSettings);
                }
            }
        }

        // 「タブで開く」メニューのクリックイベントハンドラ
        private void OpenInNewTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is FavoriteFolderModel folder && !string.IsNullOrEmpty(folder.Path))
            {
                // EventAggregator を使用してイベントを発行
                _eventAggregator?.GetEvent<OpenInNewTabEvent>().Publish(
                    new OpenInNewTabEventArgs(folder.Path, CONTROL_ID)); // SourceId を追加
            }
        }

        // 表示名を解除するメニューのクリックイベントハンドラ
        private void RemoveDisplayName_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is FavoriteFolderModel folder)
            {
                folder.DisplayName = null; // 表示名をクリア
                SetCurrentSettings();
                SettingsHelper.SaveSettings(_appSettings);
            }
        }

        // お気に入りから削除するメニューのクリックイベントハンドラ (CommandParameter の型を変更)
        private void RemoveFromFavorites_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is FavoriteFolderModel folder)
            {
                RemoveFavoriteFolder(folder); // 修正されたメソッドを呼び出す
            }
        }

        // FileSystemTreeView.xaml.cs からコピーしたヘルパーメソッド
        private TreeViewItem GetTreeViewItemForItem(ItemsControl parent, object item)
        {
            if (parent == null || item == null)
                return null;

            if (parent.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                return null;

            var container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            if (container != null)
                return container;

            // 子アイテムを再帰的に検索 (お気に入りフォルダは通常フラットだが念のため)
            for (int i = 0; i < parent.Items.Count; i++)
            {
                var childContainer = parent.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (childContainer == null)
                    continue;

                // TreeViewItem が展開されている場合のみ子を検索 (お気に入りでは不要かもしれない)
                // if (childContainer.IsExpanded)
                // {
                    var result = GetTreeViewItemForItem(childContainer, item);
                    if (result != null)
                        return result;
                // }
            }

            return null;
        }


        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject);
        }

        private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is FavoriteFolderModel folder && !string.IsNullOrEmpty(folder.Path) && Directory.Exists(folder.Path))
            {
                Process.Start("explorer.exe", $"\"{folder.Path}\"");
            }
        }
    }
}
