using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using Illustra.Helpers;
using System.IO;
using Illustra.Events;
using System.Diagnostics;

namespace Illustra.Views
{
    public partial class FavoriteFoldersControl : UserControl, IActiveAware
    {
        private Point _startPoint;
        private bool _isDragging;
        private string? _draggedItem;
        private ObservableCollection<string> _favoriteFolders;
        private AppSettings _appSettings;
        private IEventAggregator? _eventAggregator;
        private bool ignoreSelectedChangedOnce;
        private const string CONTROL_ID = "FavoriteFolders";

        #region IActiveAware Implementation
#pragma warning disable 0067 // 使用されていませんという警告を無視
        public bool IsActive { get; set; }
        public event EventHandler? IsActiveChanged;
#pragma warning restore 0067 // 警告の無視を終了
        #endregion

        public FavoriteFoldersControl()
        {
            InitializeComponent();
            Loaded += FavoriteFoldersControl_Loaded;

            // 設定を読み込む
            _appSettings = SettingsHelper.GetSettings();

            // お気に入りフォルダの初期化
            _favoriteFolders = _appSettings.FavoriteFolders;
            FavoriteFoldersTreeView.ItemsSource = _favoriteFolders;
        }

        private void FavoriteFoldersControl_Loaded(object sender, RoutedEventArgs e)
        {
            // ContainerLocatorを使ってEventAggregatorを取得
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視

            // お気に入り関連イベントの設定
            _eventAggregator.GetEvent<AddToFavoritesEvent>().Subscribe(AddFavoriteFolder);
            _eventAggregator.GetEvent<RemoveFromFavoritesEvent>().Subscribe(RemoveFavoriteFolder);
        }

        private void OnFolderSelected(FolderSelectedEventArgs args)
        {
            if (args.Path == (string)FavoriteFoldersTreeView.SelectedItem) return;
            if (_favoriteFolders.Contains(args.Path))
            {
                var item = FavoriteFoldersTreeView.ItemContainerGenerator.ContainerFromItem(args.Path) as TreeViewItem;
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

        public void SaveAllData()
        {
            // お気に入りフォルダの設定を保存
            _appSettings.FavoriteFolders = _favoriteFolders;
            SettingsHelper.SaveSettings(_appSettings);
        }

        private void FavoriteFoldersTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (ignoreSelectedChangedOnce)
            {
                ignoreSelectedChangedOnce = false;
                return;
            }

            ignoreSelectedChangedOnce = false;
            if (e.NewValue is string path && !string.IsNullOrEmpty(path))
            {
                if (Directory.Exists(path))
                {
                    // フォルダ選択イベントを発行
                    Debug.WriteLine($"FavoriteFoldersTreeView: SelectedItemChanged: Publish Events: {path}");
                    _eventAggregator?.GetEvent<FolderSelectedEvent>().Publish(
                        new FolderSelectedEventArgs(path, CONTROL_ID));
                    _eventAggregator?.GetEvent<SelectFileRequestEvent>().Publish("");
                }
            }
        }

        private void FavoriteFolders_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
            _isDragging = false;
            _draggedItem = null;
        }

        private void FavoriteFolders_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            if (_isDragging) return;

            Point position = e.GetPosition(null);
            if (Math.Abs(position.X - _startPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _startPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var selectedItem = FavoriteFoldersTreeView.SelectedItem as string;
            if (selectedItem == null) return;

            // ドラッグ開始
            _isDragging = true;
            _draggedItem = selectedItem;

            var dragData = new DataObject("FavoriteFolder", selectedItem);
            DragDrop.DoDragDrop(FavoriteFoldersTreeView, dragData, DragDropEffects.Move);
        }

        private void FavoriteFolders_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("FavoriteFolder"))
            {
                if (sender != e.Source)
                {
                    e.Effects = DragDropEffects.None;
                }
                else
                {
                    e.Effects = DragDropEffects.Move;
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // ドロップ先のTreeViewItemを取得
                var targetItem = GetDropTargetItem(e.OriginalSource);
                if (targetItem != null && targetItem.DataContext is string targetPath && Directory.Exists(targetPath))
                {
                    e.Effects = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey
                        ? DragDropEffects.Copy
                        : DragDropEffects.Move;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void FavoriteFolders_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("FavoriteFolder"))
            {
                // お気に入りフォルダの並び替え処理（既存のコード）
                string? sourceItem = e.Data.GetData("FavoriteFolder") as string;
                if (sourceItem == null) return;

                var treeView = sender as TreeView;
                if (treeView == null) return;

                Point position = e.GetPosition(treeView);
                var result = VisualTreeHelper.HitTest(treeView, position);
                if (result == null) return;

                var obj = result.VisualHit;
                while (obj != null && !(obj is TreeViewItem) && !(obj is TreeView))
                {
                    obj = VisualTreeHelper.GetParent(obj);
                }

                if (obj is TreeViewItem item)
                {
                    string? targetItem = item.DataContext as string;
                    if (targetItem == null) return;

                    if (sourceItem == targetItem) return;

                    ReorderFavoriteFolders(sourceItem, targetItem);
                }
                else
                {
                    if (_favoriteFolders.Contains(sourceItem))
                    {
                        _favoriteFolders.Remove(sourceItem);
                        _favoriteFolders.Add(sourceItem);
                    }
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // ファイルのドロップ処理
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;

                // ドロップ先のTreeViewItemを取得
                var targetItem = GetDropTargetItem(e.OriginalSource);
                if (targetItem == null || !(targetItem.DataContext is string targetPath) || !Directory.Exists(targetPath))
                    return;

                bool isCopy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
                var fileOperationHelper = new FileOperationHelper();
                await fileOperationHelper.ExecuteFileOperation(files.ToList(), targetPath, isCopy);
            }
        }

        private TreeViewItem? GetDropTargetItem(object originalSource)
        {
            if (originalSource is DependencyObject depObj)
            {
                var item = depObj as TreeViewItem;
                while (item == null && depObj != null)
                {
                    depObj = VisualTreeHelper.GetParent(depObj);
                    item = depObj as TreeViewItem;
                }
                return item;
            }
            return null;
        }

        private void ReorderFavoriteFolders(string sourceItem, string targetItem)
        {
            if (!_favoriteFolders.Contains(sourceItem) || !_favoriteFolders.Contains(targetItem))
                return;

            var index = _favoriteFolders.IndexOf(targetItem);
            _favoriteFolders.Remove(sourceItem);
            _favoriteFolders.Insert(index, sourceItem);

            FavoriteFoldersTreeView.ItemsSource = null;
            FavoriteFoldersTreeView.ItemsSource = _favoriteFolders;
        }

        public ObservableCollection<string> FavoriteFoldersList
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
            if (!_favoriteFolders.Contains(folderPath))
            {
                _favoriteFolders.Add(folderPath);
                FavoriteFoldersTreeView.ItemsSource = _favoriteFolders;
            }
        }

        public void RemoveFavoriteFolder(string folderPath)
        {
            if (_favoriteFolders.Contains(folderPath))
            {
                _favoriteFolders.Remove(folderPath);
                FavoriteFoldersTreeView.ItemsSource = _favoriteFolders;
            }
        }

        public ObservableCollection<string> FavoriteFolders => _favoriteFolders;
    }

}
