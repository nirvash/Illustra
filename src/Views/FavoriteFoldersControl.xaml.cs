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
// using Illustra.Views; // ProgressDialog は DialogHelper で使うので不要
using System.Threading.Tasks; // Task を使うために追加
using System; // IProgress を使うために追加
// using MahApps.Metro.Controls; // MetroWindow は DialogHelper で使うので不要
using Illustra.Helpers; // DialogHelper を使うために追加
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

        private ObservableCollection<FavoriteFolderModel> ConvertToModels(ObservableCollection<string> paths)
        {
            var models = new ObservableCollection<FavoriteFolderModel>();
            foreach (var path in paths)
            {
                models.Add(new FavoriteFolderModel(path));
            }
            return models;
        }

        private ObservableCollection<string> ConvertToPaths(ObservableCollection<FavoriteFolderModel> models)
        {
            var paths = new ObservableCollection<string>();
            foreach (var model in models)
            {
                paths.Add(model.Path);
            }
            return paths;
        }

        #region IActiveAware Implementation
#pragma warning disable 0067 // 使用されていませんという警告を無視
        public bool IsActive { get; set; }
        public event EventHandler? IsActiveChanged;
#pragma warning restore 0067 // 警告の無視を終了
        #endregion

        public class CustomDropHandler : DefaultDropHandler
        {
            private readonly FavoriteFoldersControl _parent = null;

            public CustomDropHandler(FavoriteFoldersControl parent)
            {
                _parent = parent;
            }

            public override void DragOver(IDropInfo e)
            {
                base.DragOver(e);
                try
                {
                    // お気に入りリスト並び替え
                    if (e.Data is FavoriteFolderModel)
                    {
                        var isTreeViewItem = e.InsertPosition.HasFlag(RelativeInsertPosition.TargetItemCenter) && e.VisualTargetItem is TreeViewItem;
                        if (isTreeViewItem)
                        {
                            e.Effects = DragDropEffects.Move;
                            e.DropTargetHintAdorner = DropTargetAdorners.Insert;
                            e.DropTargetHintState = DropHintState.Error;
                        }
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

                    // 同じフォルダへのドロップは禁止
                    if (DragDropHelper.IsSameDirectory(files, targetModel.Path))
                    {
                        e.Effects = DragDropEffects.None;
                        return;
                    }

                    e.Effects = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey
                        ? DragDropEffects.Copy
                        : DragDropEffects.Move;
                    e.DropTargetAdorner = DropTargetAdorners.Highlight;
                }
                catch (Exception)
                {
                    e.Effects = DragDropEffects.None;
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
            }
        }

        public FavoriteFoldersControl()
        {
            InitializeComponent();
            Loaded += FavoriteFoldersControl_Loaded;

            // 設定を読み込む
            _appSettings = SettingsHelper.GetSettings();

            // お気に入りフォルダの初期化
            _favoriteFolders = ConvertToModels(_appSettings.FavoriteFolders);
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
            _eventAggregator.GetEvent<RemoveFromFavoritesEvent>().Subscribe(RemoveFavoriteFolder);

            GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(FavoriteFoldersTreeView, new CustomDropHandler(this));
            GongSolutions.Wpf.DragDrop.DragDrop.SetDragHandler(FavoriteFoldersTreeView, new DefaultDragHandler());

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
            _appSettings.FavoriteFolders = ConvertToPaths(_favoriteFolders);
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

        public void RemoveFavoriteFolder(string folderPath)
        {
            var folder = _favoriteFolders.FirstOrDefault(f => f.Path == folderPath);
            if (folder != null)
            {
                _favoriteFolders.Remove(folder);
                SetCurrentSettings();
                SettingsHelper.SaveSettings(_appSettings);
            }
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

        private void FavoriteFoldersTreeView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var treeView = sender as TreeView;
            if (treeView == null) return;

            // イベントの発生元を見つける
            if (e.OriginalSource is DependencyObject source)
            {
                var treeViewItem = FindVisualParent<TreeViewItem>(source);
                if (treeViewItem != null)
                {
                    var selectedFolder = treeViewItem.DataContext as FavoriteFolderModel;
                    if (selectedFolder != null && !string.IsNullOrEmpty(selectedFolder.Path))
                    {
                        var removeMenuItem = treeView.ContextMenu?.Items.OfType<MenuItem>()
                            .FirstOrDefault(x => x.Name == "RemoveFromFavoritesMenuItem");

                        if (removeMenuItem != null)
                        {
                            removeMenuItem.CommandParameter = selectedFolder.Path;
                        }
                    }
                }
            }
        }

        private void RemoveFromFavorites_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is string path)
            {
                RemoveFavoriteFolder(path);
            }
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject);
        }
    }
}
