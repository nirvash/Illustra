using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.ViewModels;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Collections.ObjectModel;
using Illustra.Events;
using Prism.Events;

namespace Illustra.Views
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly IEventAggregator _eventAggregator;
        private bool _isInitialized = false;
        private AppSettings _appSettings;
        private string _currentSelectedFilePath = string.Empty;

        private DateTime _lastLoadTime = DateTime.MinValue;
        private readonly object _loadLock = new object();
        private bool _sortByDate = true;
        private bool _sortAscending = true;
        private double _mainSplitterPosition;
        private double _favoritesFoldersSplitterPosition;
        private FavoriteFoldersControl? _favoriteFoldersControl;
        private FolderTreeControl? _folderTreeControl;
        private string? _currentFolderPath = null;

        public MainWindow(IEventAggregator eventAggregator)
        {
            InitializeComponent();
            _eventAggregator = eventAggregator;

            // 設定を読み込む
            _appSettings = SettingsHelper.GetSettings();

            // 設定を適用
            ApplySettings();

            // ウィンドウサイズと位置を設定から復元
            Width = _appSettings.WindowWidth;
            Height = _appSettings.WindowHeight;
            Left = _appSettings.WindowLeft;
            Top = _appSettings.WindowTop;
            WindowState = _appSettings.WindowState;

            // スプリッター位置を復元
            _mainSplitterPosition = _appSettings.MainSplitterPosition;
            _favoritesFoldersSplitterPosition = _appSettings.FavoriteFoldersHeight;
            RestoreSplitterPositions();

            // ウィンドウが閉じられるときに設定を保存
            Closing += MainWindow_Closing;

            // ウィンドウがロードされた後に前回のフォルダを選択
            Loaded += MainWindow_Loaded;

            // プロパティ領域を初期化
            ClearPropertiesDisplay();

            // イベントを購読
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected);

            // コントロールのインスタンスを取得
            _favoriteFoldersControl = FavoriteFolders;
            _folderTreeControl = FolderTree;

            _isInitialized = true;
        }

        /// <summary>
        /// プロパティ表示をクリアするメソッド
        /// </summary>
        public void ClearPropertiesDisplay()
        {
            PropertyPanel.ImageProperties = null;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 前回開いていたフォルダがある場合、少し時間をおいてから処理開始
            if (!string.IsNullOrEmpty(_appSettings.LastFolderPath) && System.IO.Directory.Exists(_appSettings.LastFolderPath))
            {
                // UIが完全にロードされてから処理を行う
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        var path = _appSettings.LastFolderPath;
                        // ツリービューが完全に構築されるのを待つ
                        await Task.Delay(500);
                        _eventAggregator.GetEvent<FolderSelectedEvent>().Publish(path);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"前回のフォルダを開く際にエラーが発生: {ex}");
                    }
                }));
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 現在のウィンドウ状態を保存
            if (WindowState == WindowState.Normal)
            {
                _appSettings.WindowWidth = Width;
                _appSettings.WindowHeight = Height;
                _appSettings.WindowLeft = Left;
                _appSettings.WindowTop = Top;
            }

            _appSettings.WindowState = WindowState;



            // 現在のフォルダパスを保存
            _appSettings.LastFolderPath = _currentFolderPath;

            // 現在の選択ファイルパスを保存
            _appSettings.LastSelectedFilePath = _currentSelectedFilePath;

            // ソート順の設定を保存
            _appSettings.SortByDate = _sortByDate;
            _appSettings.SortAscending = _sortAscending;

            try
            {
                // スプリッター位置を保存
                _appSettings.MainSplitterPosition = MainContentGrid.ColumnDefinitions[0].ActualWidth;
                _appSettings.FavoriteFoldersHeight = LeftPanelGrid.RowDefinitions[0].ActualHeight;
                _appSettings.PropertySplitterPosition = RightPanelGrid.RowDefinitions[3].ActualHeight;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"スプリッター位置の保存に失敗: {ex.Message}");
            }

            FavoriteFolders.SaveAllData();
            FolderTree.SaveAllData();
            ThumbnailList.SaveAllData();

            // 設定を保存
            SettingsHelper.SaveSettings(_appSettings);
        }










        // メニュー関連のメソッド
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }


        private void RefreshMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // 現在のフォルダを再読み込み
            if (!string.IsNullOrEmpty(_currentFolderPath))
            {
                ThumbnailList.LoadFileNodes(_currentFolderPath);
            }
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // 設定ウィンドウを表示
            var settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this;

            if (settingsWindow.ShowDialog() == true)
            {
                // 設定が変更された場合は反映
                ApplySettings();
            }
        }

        /// <summary>
        /// 設定をUIに適用する。メニューの変更適用にも使われている
        /// </summary>
        private void ApplySettings()
        {
            // 設定を再読み込み
            _appSettings = SettingsHelper.GetSettings();

            ThumbnailList.ApplySettings();

            // ソート順の設定を適用
            _sortByDate = _appSettings.SortByDate;
            _sortAscending = _appSettings.SortAscending;
            SortByDateMenuItem.IsChecked = _sortByDate;
            SortByNameMenuItem.IsChecked = !_sortByDate;
            SortAscendingMenuItem.IsChecked = _sortAscending;
            SortDescendingMenuItem.IsChecked = !_sortAscending;
        }

        private void SortByDateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _sortByDate = true;
            SortByDateMenuItem.IsChecked = true;
            SortByNameMenuItem.IsChecked = false;
            ThumbnailList.SortThumbnail(_sortByDate, _sortAscending);
        }

        private void SortByNameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _sortByDate = false;
            SortByDateMenuItem.IsChecked = false;
            SortByNameMenuItem.IsChecked = true;
            ThumbnailList.SortThumbnail(_sortByDate, _sortAscending);
        }

        private void SortOrderMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender == SortAscendingMenuItem)
            {
                _sortAscending = true;
                SortAscendingMenuItem.IsChecked = true;
                SortDescendingMenuItem.IsChecked = false;
            }
            else
            {
                _sortAscending = false;
                SortAscendingMenuItem.IsChecked = false;
                SortDescendingMenuItem.IsChecked = true;
            }
            ThumbnailList.SortThumbnail(_sortByDate, _sortAscending);
        }

        public bool SortByDate
        {
            get => _sortByDate;
            set
            {
                if (_sortByDate != value)
                {
                    _sortByDate = value;
                    OnPropertyChanged(nameof(SortByDate));
                }
            }
        }

        public bool SortAscending
        {
            get => _sortAscending;
            set
            {
                if (_sortAscending != value)
                {
                    _sortAscending = value;
                    OnPropertyChanged(nameof(SortAscending));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void DisplayGeneratedItemsInfo(ListView listView)
        {
            int totalItems = listView.Items.Count;
            int generatedItems = GetGeneratedItemsCount(listView);

            Debug.WriteLine($"全アイテム数: {totalItems}");
            Debug.WriteLine($"生成されたアイテム数: {generatedItems}");
            Debug.WriteLine($"仮想化率: {(1 - (double)generatedItems / totalItems) * 100:F2}%");
        }

        /// <summary>
        /// Gets the number of items that have been generated (realized) by the virtualization system
        /// </summary>
        private int GetGeneratedItemsCount(ListView listView)
        {
            int count = 0;

            if (listView == null)
                return 0;

            for (int i = 0; i < listView.Items.Count; i++)
            {
                var container = listView.ItemContainerGenerator.ContainerFromIndex(i);
                if (container != null)
                {
                    count++;
                }
            }

            return count;
        }

        private void AddToFavorites_Click(object sender, RoutedEventArgs e)
        {
            if (_folderTreeControl.FolderTreeView.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string path)
            {
                FavoriteFolders.AddFavoriteFolder(path);
            }
        }

        private void RemoveFromFavorites_Click(object sender, RoutedEventArgs e)
        {
            if (_folderTreeControl.FolderTreeView.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string path)
            {
                FavoriteFolders.RemoveFavoriteFolder(path);
            }
        }

        private void RestoreSplitterPositions()
        {
            try
            {
                // 左側パネルと右側パネルの分割（メインスプリッター）
                if (_mainSplitterPosition > 0)
                {
                    MainContentGrid.ColumnDefinitions[0].Width = new GridLength(_mainSplitterPosition, GridUnitType.Pixel);
                }

                // お気に入りツリーとフォルダツリーの分割
                if (_favoritesFoldersSplitterPosition > 0)
                {
                    LeftPanelGrid.RowDefinitions[0].Height = new GridLength(_favoritesFoldersSplitterPosition, GridUnitType.Pixel);
                }

                // プロパティパネルの高さ
                if (_appSettings.PropertySplitterPosition > 0)
                {
                    RightPanelGrid.RowDefinitions[3].Height = new GridLength(_appSettings.PropertySplitterPosition, GridUnitType.Pixel);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"スプリッター位置の復元に失敗: {ex.Message}");
            }
        }

        private void OnFolderSelected(string path)
        {
            if (path == _currentFolderPath) return;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                _currentFolderPath = path;
                ThumbnailList.LoadFileNodes(path);
                ClearPropertiesDisplay();
            }
        }

    }
}
