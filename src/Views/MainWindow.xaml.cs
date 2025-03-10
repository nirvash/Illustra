using System.IO;
using System.Windows;
using System.Windows.Controls; // Add this for MenuItem
using Illustra.Helpers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Illustra.Events;
using System.Threading.Tasks;
using System;
using Illustra.ViewModels;

namespace Illustra.Views
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly IEventAggregator _eventAggregator;
        private AppSettings _appSettings;
        private bool _sortByDate = true;
        private bool _sortAscending = true;
        private double _mainSplitterPosition;
        private double _favoritesFoldersSplitterPosition;
        private FavoriteFoldersControl? _favoriteFoldersControl;
        private FolderTreeControl? _folderTreeControl;
        private string? _currentFolderPath = null;
        private const string CONTROL_ID = "MainWindow";

        // ソートメニューアイテム
        private MenuItem? _sortByDateAscendingMenuItem;
        private MenuItem? _sortByDateDescendingMenuItem;
        private MenuItem? _sortByNameAscendingMenuItem;
        private MenuItem? _sortByNameDescendingMenuItem;

        private readonly MainWindowViewModel _viewModel;

        public MainWindow(IEventAggregator eventAggregator, MainWindowViewModel viewModel)
        {
            InitializeComponent();
            _eventAggregator = eventAggregator;
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            // メニューアイテムの初期化
            InitializeSortMenuItems();

            // 設定を読み込む
            _appSettings = SettingsHelper.GetSettings();

            // イベントを購読（コンストラクタで設定）
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視

            // コントロールのインスタンスを取得（ここに移動）
            _favoriteFoldersControl = FavoriteFolders;
            _folderTreeControl = FolderTree;

            // ウィンドウがロードされた後に前回のフォルダを選択
            Loaded += MainWindow_Loaded;
            // ウィンドウが閉じられるときに設定を保存
            Closing += MainWindow_Closing;

            // 設定を適用（ウィンドウ設定のみ）
            Width = _appSettings.WindowWidth;
            Height = _appSettings.WindowHeight;
            Left = _appSettings.WindowLeft;
            Top = _appSettings.WindowTop;
            WindowState = _appSettings.WindowState;

            // スプリッター位置を復元
            _mainSplitterPosition = _appSettings.MainSplitterPosition;
            _favoritesFoldersSplitterPosition = _appSettings.FavoriteFoldersHeight;

            // プロパティ領域を初期化
            ClearPropertiesDisplay();

            DataContext = _viewModel;

            // バージョン情報をタイトルに追加
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var versionString = $"{version.Major}.{version.Minor}.{version.Build}";
            Title += $" {versionString}";
        }

        private void InitializeSortMenuItems()
        {
            // メニューアイテムの作成と設定
            _sortByDateAscendingMenuItem = FindName("SortByDateAscendingMenuItem") as MenuItem;
            _sortByDateDescendingMenuItem = FindName("SortByDateDescendingMenuItem") as MenuItem;
            _sortByNameAscendingMenuItem = FindName("SortByNameAscendingMenuItem") as MenuItem;
            _sortByNameDescendingMenuItem = FindName("SortByNameDescendingMenuItem") as MenuItem;

            // イベントハンドラの設定
            if (_sortByDateAscendingMenuItem != null)
                _sortByDateAscendingMenuItem.Click += OnDateSortChanged;
            if (_sortByDateDescendingMenuItem != null)
                _sortByDateDescendingMenuItem.Click += OnDateSortChanged;
            if (_sortByNameAscendingMenuItem != null)
                _sortByNameAscendingMenuItem.Click += OnNameSortChanged;
            if (_sortByNameDescendingMenuItem != null)
                _sortByNameDescendingMenuItem.Click += OnNameSortChanged;
        }

        /// <summary>
        /// プロパティ表示をクリアするメソッド
        /// </summary>
        public void ClearPropertiesDisplay()
        {
            PropertyPanel.ImageProperties = null;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // スプリッター位置の復元はUIスレッドで行う
            RestoreSplitterPositions();

            // UIスレッドで設定を適用
            ApplySettings();

            // 非同期処理が必要な他の初期化はここで行う
            await Task.Run(() =>
            {
                // UIに関係ない非同期処理をここに書く
            });
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
            _appSettings.LastFolderPath = _currentFolderPath ?? "";

            // ソート順の設定を保存
            _appSettings.SortByDate = _sortByDate;
            _appSettings.SortAscending = _sortAscending;

            try
            {
                // スプリッター位置を保存
                _appSettings.MainSplitterPosition = MainContentGrid.ColumnDefinitions[0].ActualWidth;
                _appSettings.FavoriteFoldersHeight = LeftPanelGrid.RowDefinitions[0].ActualHeight;
                _appSettings.PropertySplitterPosition = RightPanelGrid.RowDefinitions[2].ActualHeight;
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
            Application.Current.Shutdown();
        }

        private void ShowAboutDialog(object sender, RoutedEventArgs e)
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var versionString = $"{version.Major}.{version.Minor}.{version.Build}";
            var productVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).ProductVersion;

            var versionInfo = $"バージョン: \n{versionString}\n\n製品バージョン: \n{productVersion}";
            Clipboard.SetText(versionInfo);
            MessageBox.Show(versionInfo, "バージョン情報", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private async void SortByDateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _sortByDate = true;
            SortByDateMenuItem.IsChecked = true;
            SortByNameMenuItem.IsChecked = false;
            await ThumbnailList.SortThumbnailAsync(_sortByDate, _sortAscending);
        }

        private async void SortByNameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _sortByDate = false;
            SortByDateMenuItem.IsChecked = false;
            SortByNameMenuItem.IsChecked = true;
            await ThumbnailList.SortThumbnailAsync(_sortByDate, _sortAscending);
        }

        private async void SortOrderMenuItem_Click(object sender, RoutedEventArgs e)
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
            await ThumbnailList.SortThumbnailAsync(_sortByDate, _sortAscending);
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

        private void AddToFavorites_Click(object sender, RoutedEventArgs e)
        {
            // 新しいお気に入りに追加イベントを発行する方法に変更
            if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
            {
                _eventAggregator.GetEvent<AddToFavoritesEvent>().Publish(_currentFolderPath);
            }
        }

        private void RemoveFromFavorites_Click(object sender, RoutedEventArgs e)
        {
            // 新しいお気に入りから削除イベントを発行する方法に変更
            if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
            {
                _eventAggregator.GetEvent<RemoveFromFavoritesEvent>().Publish(_currentFolderPath);
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
                    RightPanelGrid.RowDefinitions[2].Height = new GridLength(_appSettings.PropertySplitterPosition, GridUnitType.Pixel);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"スプリッター位置の復元に失敗: {ex.Message}");
            }
        }

        private void OnFolderSelected(FolderSelectedEventArgs args)
        {
            if (args.Path == _currentFolderPath) return;
            if (!string.IsNullOrEmpty(args.Path) && Directory.Exists(args.Path))
            {
                _currentFolderPath = args.Path;
                ClearPropertiesDisplay();

            }
        }

        private async void OnDateSortChanged(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            if (menuItem == null) return;

            menuItem.IsChecked = true;

            // 相互に排他的なメニュー項目のチェックを解除
            if (menuItem == _sortByDateAscendingMenuItem)
            {
                if (_sortByDateDescendingMenuItem != null)
                    _sortByDateDescendingMenuItem.IsChecked = false;
                if (_sortByNameAscendingMenuItem != null)
                    _sortByNameAscendingMenuItem.IsChecked = false;
                if (_sortByNameDescendingMenuItem != null)
                    _sortByNameDescendingMenuItem.IsChecked = false;

                await ThumbnailList.SortThumbnailAsync(true, true);
                _appSettings.SortByDate = true;
                _appSettings.SortAscending = true;
            }
            else if (menuItem == _sortByDateDescendingMenuItem)
            {
                if (_sortByDateAscendingMenuItem != null)
                    _sortByDateAscendingMenuItem.IsChecked = false;
                if (_sortByNameAscendingMenuItem != null)
                    _sortByNameAscendingMenuItem.IsChecked = false;
                if (_sortByNameDescendingMenuItem != null)
                    _sortByNameDescendingMenuItem.IsChecked = false;

                await ThumbnailList.SortThumbnailAsync(true, false);
                _appSettings.SortByDate = true;
                _appSettings.SortAscending = false;
            }
        }

        private async void OnNameSortChanged(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            if (menuItem == null) return;

            menuItem.IsChecked = true;

            // 相互に排他的なメニュー項目のチェックを解除
            if (menuItem == _sortByNameAscendingMenuItem)
            {
                if (_sortByDateAscendingMenuItem != null)
                    _sortByDateAscendingMenuItem.IsChecked = false;
                if (_sortByDateDescendingMenuItem != null)
                    _sortByDateDescendingMenuItem.IsChecked = false;
                if (_sortByNameDescendingMenuItem != null)
                    _sortByNameDescendingMenuItem.IsChecked = false;

                await ThumbnailList.SortThumbnailAsync(false, true);
                _appSettings.SortByDate = false;
                _appSettings.SortAscending = true;
            }
            else if (menuItem == _sortByNameDescendingMenuItem)
            {
                if (_sortByDateAscendingMenuItem != null)
                    _sortByDateAscendingMenuItem.IsChecked = false;
                if (_sortByDateDescendingMenuItem != null)
                    _sortByDateDescendingMenuItem.IsChecked = false;
                if (_sortByNameAscendingMenuItem != null)
                    _sortByNameAscendingMenuItem.IsChecked = false;

                await ThumbnailList.SortThumbnailAsync(false, false);
                _appSettings.SortByDate = false;
                _appSettings.SortAscending = false;
            }
        }
    }
}
