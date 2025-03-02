using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using Illustra.Helpers;
using System.IO;
using Illustra.Events;

namespace Illustra.Views
{
    public partial class FavoriteFoldersControl : UserControl, IActiveAware
    {
        private Point _startPoint;
        private bool _isDragging;
        private string? _draggedItem;
        private ObservableCollection<string> _favoriteFolders;
        private AppSettings _appSettings;
        private IEventAggregator _eventAggregator;

        #region IActiveAware Implementation
        public bool IsActive { get; set; }
        public event EventHandler IsActiveChanged;
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

            // ドラッグ&ドロップイベントの設定
            FavoriteFoldersTreeView.PreviewMouseLeftButtonDown += FavoriteFolders_PreviewMouseLeftButtonDown;
            FavoriteFoldersTreeView.PreviewMouseMove += FavoriteFolders_PreviewMouseMove;
            FavoriteFoldersTreeView.DragOver += FavoriteFolders_DragOver;
            FavoriteFoldersTreeView.Drop += FavoriteFolders_Drop;
        }

        private void OnFolderSelected(string path)
        {
            if (path == FavoriteFoldersTreeView.SelectedItem) return;
            if (_favoriteFolders.Contains(path))
            {
                var item = FavoriteFoldersTreeView.ItemContainerGenerator.ContainerFromItem(path) as TreeViewItem;
                if (item != null)
                {
                    item.IsSelected = true;
                    item.Focus();
                }
            }
        }

        private void FavoriteFoldersControl_Loaded(object sender, RoutedEventArgs e)
        {
            // ContainerLocatorを使ってEventAggregatorを取得
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected);
        }

        public void SaveAllData()
        {
            // お気に入りフォルダの設定を保存
            _appSettings.FavoriteFolders = _favoriteFolders;
            SettingsHelper.SaveSettings(_appSettings);
        }

        private void FavoriteFoldersTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is string path && !string.IsNullOrEmpty(path))
            {
                if (Directory.Exists(path))
                {
                    // フォルダ選択イベントを発行
                    _eventAggregator.GetEvent<FolderSelectedEvent>().Publish(path);
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
            if (!e.Data.GetDataPresent("FavoriteFolder") || sender != e.Source)
            {
                e.Effects = DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.Move;
            }
            e.Handled = true;
        }

        private void FavoriteFolders_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("FavoriteFolder"))
            {
                string sourceItem = e.Data.GetData("FavoriteFolder") as string;
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
                    string targetItem = item.DataContext as string;
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
    }

}
