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
using System.Collections.Generic;
using DryIoc.ImTools;

namespace Illustra.Views
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// このクラスはpartialクラスとして実装されており、プロパティの定義はMainWindow.Properties.csに分離されています。
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
        private string _currentFolderPath = string.Empty;
        private const string CONTROL_ID = "MainWindow";
        public bool EnableCyclicNavigation => App.Instance.EnableCyclicNavigation;

        // ソートメニューアイテム
        private MenuItem? _sortByDateAscendingMenuItem;
        private MenuItem? _sortByDateDescendingMenuItem;
        private MenuItem? _sortByNameAscendingMenuItem;
        private MenuItem? _sortByNameDescendingMenuItem;

        private readonly MainWindowViewModel _viewModel;

        public MainWindow(IEventAggregator eventAggregator, MainWindowViewModel viewModel)
        {
            // InitializeComponentはXAMLから自動生成されるメソッドで、
            // リンターエラーが表示されることがありますが、ビルド時には問題ありません
            InitializeComponent();
            _eventAggregator = eventAggregator;
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;

            // 設定の読み込み
            _appSettings = SettingsHelper.GetSettings();

            // イベント購読
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected);
            _eventAggregator.GetEvent<FilterChangedEvent>().Subscribe(OnFilterChanged, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視);
            _eventAggregator.GetEvent<SortOrderChangedEvent>().Subscribe(OnSortOrderChanged, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視);

            // FavoriteFoldersとFolderTreeはXAMLで定義されたコンポーネントで、
            // リンターエラーが表示されることがありますが、ビルド時には問題ありません
            _favoriteFoldersControl = FavoriteFolders;
            _folderTreeControl = FolderTree;

            // ウィンドウがロードされた後に前回のフォルダを選択
            Loaded += MainWindow_Loaded;
            // ウィンドウが閉じられるときに設定を保存
            Closing += MainWindow_Closing;

            // ソートメニューの初期化
            InitializeSortMenuItems();

            // 設定の適用
            ApplySettings();

            // ToggleCyclicNavigationはXAMLで定義されたコンポーネントで、
            // リンターエラーが表示されることがありますが、ビルド時には問題ありません
            ToggleCyclicNavigation.IsChecked = App.Instance.EnableCyclicNavigation;

            // スプリッター位置を復元
            _mainSplitterPosition = _appSettings.MainSplitterPosition;
            _favoritesFoldersSplitterPosition = _appSettings.FavoriteFoldersHeight;
            RestoreSplitterPositions();

            // バージョン情報をタイトルに追加
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var versionString = $"{version.Major}.{version.Minor}.{version.Build}";
            Title += $" {versionString}";

            // メニュー項目のクリックイベントハンドラを設定
            var languageMenuItem = FindName("LanguageMenuItem") as MenuItem;
            var shortcutMenuItem = FindName("ShortcutMenuItem") as MenuItem;
            if (languageMenuItem != null)
            {
                languageMenuItem.Click += (s, e) => _viewModel.OpenLanguageSettingsCommand.Execute();
            }
            if (shortcutMenuItem != null)
            {
                shortcutMenuItem.Click += (s, e) => _viewModel.OpenShortcutSettingsCommand.Execute();
            }
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

        public void UpdateToolsMenuVisibility()
        {
            var settings = SettingsHelper.GetSettings();
            ToolsMenu.Visibility = settings.DeveloperMode ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearAllFilters(object sender, RoutedEventArgs e)
        {
            IsTagFilterEnabled = false;
            _tagFilters.Clear();
            _currentRatingFilter = 0;
            IsPromptFilterEnabled = false;

            // フィルタ変更イベントを発行（すべてのフィルタをクリア）
            _eventAggregator.GetEvent<FilterChangedEvent>().Publish(
                new FilterChangedEventArgsBuilder(CONTROL_ID).Clear().Build());

            // フィルタクリアメニューの有効/無効を更新
            UpdateFilterMenu();
            UpdateStatusBar();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // スプリッター位置の復元はUIスレッドで行う
            RestoreSplitterPositions();

            // UIスレッドで設定を適用
            ApplySettings();

            // ツールメニューの表示/非表示を設定
            UpdateToolsMenuVisibility();

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
            _appSettings.EnableCyclicNavigation = App.Instance.EnableCyclicNavigation;

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
        private void ToggleCyclicNavigation_Click(object sender, RoutedEventArgs e)
        {
            App.Instance.EnableCyclicNavigation = ToggleCyclicNavigation.IsChecked;
            _appSettings.EnableCyclicNavigation = App.Instance.EnableCyclicNavigation;
            SettingsHelper.SaveSettings(_appSettings);
        }

        private void FilterPromptMenuItem_Click(object sender, RoutedEventArgs e)
        {
            IsPromptFilterEnabled = !IsPromptFilterEnabled;
            UpdateFilterMenu();

            // フィルタ変更イベントを発行
            _eventAggregator.GetEvent<FilterChangedEvent>().Publish(
                new FilterChangedEventArgsBuilder(CONTROL_ID)
                .WithPromptFilter(IsPromptFilterEnabled).Build());
            UpdateStatusBar();
        }

        private void FilterTagMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // 複数タグに対応したコンストラクタを使用
            var dialog = new TagFilterDialog(new List<string>(_tagFilters));
            if (dialog.ShowDialog() == true)
            {
                IsTagFilterEnabled = dialog.TagFilters.Count > 0;
                _tagFilters = new List<string>(dialog.TagFilters);
                UpdateFilterMenu();

                // フィルタ変更イベントを発行（新しいリストのインスタンスを作成）
                _eventAggregator.GetEvent<FilterChangedEvent>().Publish(
                    new FilterChangedEventArgsBuilder(CONTROL_ID)
                    .WithTagFilter(IsTagFilterEnabled, new List<string>(_tagFilters)).Build());
            }
            UpdateStatusBar();
        }

        private void FilterRating1MenuItem_Click(object sender, RoutedEventArgs e)
        {
            ApplyRatingFilter(1);
        }

        private void FilterRating2MenuItem_Click(object sender, RoutedEventArgs e)
        {
            ApplyRatingFilter(2);
        }

        private void FilterRating3MenuItem_Click(object sender, RoutedEventArgs e)
        {
            ApplyRatingFilter(3);
        }

        private void FilterRating4MenuItem_Click(object sender, RoutedEventArgs e)
        {
            ApplyRatingFilter(4);
        }

        private void FilterRating5MenuItem_Click(object sender, RoutedEventArgs e)
        {
            ApplyRatingFilter(5);
        }

        /// <summary>
        /// レーティングフィルタを適用します
        /// </summary>
        private void ApplyRatingFilter(int rating)
        {
            // 同じレーティングが選択された場合はフィルタを解除
            _currentRatingFilter = (rating == _currentRatingFilter && rating > 0) ? 0 : rating;
            UpdateFilterMenu();

            // フィルタ変更イベントを発行
            _eventAggregator.GetEvent<FilterChangedEvent>().Publish(
                new FilterChangedEventArgsBuilder(CONTROL_ID)
                .WithRatingFilter(_currentRatingFilter).Build());
            UpdateStatusBar();
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void ShowAboutDialog(object sender, RoutedEventArgs e)
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var versionString = $"{version.Major}.{version.Minor}.{version.Build}";
            var productVersion = "";
            try
            {
                string path = Environment.GetCommandLineArgs()[0];
                var ver = FileVersionInfo.GetVersionInfo(path);
                productVersion = ver.ProductVersion;
            }
            catch
            {
                productVersion = versionString;
            }
            var versionInfo = $"{(string)FindResource("String_About_Version")} \n{versionString}\n\n{(string)FindResource("String_About_ProductVersion")} \n{productVersion}";

            var dialog = new VersionInfoDialog(versionInfo);
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        /// <summary>
        /// 設定をUIに適用する。メニューの変更適用にも使われている
        /// </summary>
        private void ApplySettings()
        {
            // 設定を再読み込み
            _appSettings = SettingsHelper.GetSettings();

            RestoreWindowLocation();

            ThumbnailList.ApplySettings();
            // ソート順の設定を適用
            _sortByDate = _appSettings.SortByDate;
            _sortAscending = _appSettings.SortAscending;
            SortByDateMenuItem.IsChecked = _sortByDate;
            SortByNameMenuItem.IsChecked = !_sortByDate;
            SortAscendingMenuItem.IsChecked = _sortAscending;
            SortDescendingMenuItem.IsChecked = !_sortAscending;

            // 循環移動の設定を適用
            App.Instance.EnableCyclicNavigation = _appSettings.EnableCyclicNavigation;
            ToggleCyclicNavigation.IsChecked = App.Instance.EnableCyclicNavigation;
            SortDescendingMenuItem.IsChecked = !_sortAscending;
        }

        private void RestoreWindowLocation()
        {
            double width = _appSettings.WindowWidth;
            double height = _appSettings.WindowHeight;
            double top = _appSettings.WindowTop;
            double left = _appSettings.WindowLeft;

            // デフォルトサイズ（最初の起動や初期化用）
            if (width <= 0 || height <= 0)
            {
                width = 800;
                height = 600;
            }

            // モニタ範囲チェックして補正
            Rect virtualScreen = SystemParameters.WorkArea;

            // はみ出さないように補正
            if (left < virtualScreen.Left) left = virtualScreen.Left;
            if (top < virtualScreen.Top) top = virtualScreen.Top;
            if (left + width > virtualScreen.Right) left = virtualScreen.Right - width;
            if (top + height > virtualScreen.Bottom) top = virtualScreen.Bottom - height;

            // 最低限の幅・高さも設定
            if (width < 400) width = 400;
            if (height < 300) height = 300;

            // ウィンドウに反映
            this.Width = width;
            this.Height = height;
            this.Left = left;
            this.Top = top;
        }

        private void SortByDateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _sortByDate = true;
            SortByDateMenuItem.IsChecked = true;
            SortByNameMenuItem.IsChecked = false;

            // ソート順変更イベントを発行
            _eventAggregator.GetEvent<SortOrderChangedEvent>().Publish(
                new SortOrderChangedEventArgs(_sortByDate, _sortAscending, CONTROL_ID));
        }

        private void SortByNameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _sortByDate = false;
            SortByDateMenuItem.IsChecked = false;
            SortByNameMenuItem.IsChecked = true;

            // ソート順変更イベントを発行
            _eventAggregator.GetEvent<SortOrderChangedEvent>().Publish(
                new SortOrderChangedEventArgs(_sortByDate, _sortAscending, CONTROL_ID));
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

            // ソート順変更イベントを発行
            _eventAggregator.GetEvent<SortOrderChangedEvent>().Publish(
                new SortOrderChangedEventArgs(_sortByDate, _sortAscending, CONTROL_ID));
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

        private void OnFilterChanged(FilterChangedEventArgs args)
        {
            // タグフィルタの状態を更新（新しいリストのインスタンスを作成）
            if (args.Type == FilterChangedEventArgs.FilterChangedType.TagFilterChanged)
            {
                _isTagFilterEnabled = args.IsTagFilterEnabled;
                _tagFilters = new List<string>(args.TagFilters);
            }

            // レーティングフィルタの状態を更新
            if (args.Type == FilterChangedEventArgs.FilterChangedType.RatingFilterChanged)
                _currentRatingFilter = args.RatingFilter;

            // プロンプトフィルタの状態を更新
            if (args.Type == FilterChangedEventArgs.FilterChangedType.PromptFilterChanged)
                _isPromptFilterEnabled = args.IsPromptFilterEnabled;

            UpdateFilterMenu();
            UpdateStatusBar();
        }

        private void UpdateFilterMenu()
        {
            FilterPromptMenuItem.IsChecked = IsPromptFilterEnabled;
            FilterTagMenuItem.IsChecked = IsTagFilterEnabled;

            // 親メニューの状態を更新
            FilterRatingMenuItem.IsChecked = _currentRatingFilter > 0;

            // サブメニューの状態を更新
            FilterRating1MenuItem.IsChecked = _currentRatingFilter == 1;
            FilterRating2MenuItem.IsChecked = _currentRatingFilter == 2;
            FilterRating3MenuItem.IsChecked = _currentRatingFilter == 3;
            FilterRating4MenuItem.IsChecked = _currentRatingFilter == 4;
            FilterRating5MenuItem.IsChecked = _currentRatingFilter == 5;

            // メニュー項目の状態を更新
            FilterTagMenuItem.Header = IsTagFilterEnabled
                ? $"{FindResource("String_Menu_Filter_Tag")} ✓"
                : FindResource("String_Menu_Filter_Tag");

            // フィルタクリアメニューの有効/無効を更新
            FilterClearMenuItem.IsEnabled = _currentRatingFilter > 0 || _isPromptFilterEnabled || _isTagFilterEnabled;
        }

        private void OnFolderSelected(FolderSelectedEventArgs args)
        {
            // 現在のフォルダパスを更新
            _currentFolderPath = args.Path;

            FilterChangedEventArgs filterArgs = new FilterChangedEventArgsBuilder(CONTROL_ID)
                .Clear().Build();
            OnFilterChanged(filterArgs);

            // ステータスバーを更新
            UpdateStatusBar();
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

                _sortByDate = true;
                _sortAscending = true;
                _appSettings.SortByDate = true;
                _appSettings.SortAscending = true;

                // ソート順変更イベントを発行
                _eventAggregator.GetEvent<SortOrderChangedEvent>().Publish(
                    new SortOrderChangedEventArgs(_sortByDate, _sortAscending, CONTROL_ID));
            }
            else if (menuItem == _sortByDateDescendingMenuItem)
            {
                if (_sortByDateAscendingMenuItem != null)
                    _sortByDateAscendingMenuItem.IsChecked = false;
                if (_sortByNameAscendingMenuItem != null)
                    _sortByNameAscendingMenuItem.IsChecked = false;
                if (_sortByNameDescendingMenuItem != null)
                    _sortByNameDescendingMenuItem.IsChecked = false;

                _sortByDate = true;
                _sortAscending = false;
                _appSettings.SortByDate = true;
                _appSettings.SortAscending = false;

                // ソート順変更イベントを発行
                _eventAggregator.GetEvent<SortOrderChangedEvent>().Publish(
                    new SortOrderChangedEventArgs(_sortByDate, _sortAscending, CONTROL_ID));
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

                _sortByDate = false;
                _sortAscending = true;
                _appSettings.SortByDate = false;
                _appSettings.SortAscending = true;

                // ソート順変更イベントを発行
                _eventAggregator.GetEvent<SortOrderChangedEvent>().Publish(
                    new SortOrderChangedEventArgs(_sortByDate, _sortAscending, CONTROL_ID));
            }
            else if (menuItem == _sortByNameDescendingMenuItem)
            {
                if (_sortByDateAscendingMenuItem != null)
                    _sortByDateAscendingMenuItem.IsChecked = false;
                if (_sortByDateDescendingMenuItem != null)
                    _sortByDateDescendingMenuItem.IsChecked = false;
                if (_sortByNameAscendingMenuItem != null)
                    _sortByNameAscendingMenuItem.IsChecked = false;

                _sortByDate = false;
                _sortAscending = false;
                _appSettings.SortByDate = false;
                _appSettings.SortAscending = false;

                // ソート順変更イベントを発行
                _eventAggregator.GetEvent<SortOrderChangedEvent>().Publish(
                    new SortOrderChangedEventArgs(_sortByDate, _sortAscending, CONTROL_ID));
            }
        }

        /// <summary>
        /// ステータスバーを更新します
        /// </summary>
        private void UpdateStatusBar()
        {
            var statusItems = new List<string>();

            // 現在のフォルダパスを追加
            if (_currentFolderPath != null)
            {
                statusItems.Add($"{FindResource("String_Status_CurrentFolder")}: {_currentFolderPath}");
            }

            // レーティングフィルタが有効な場合、その情報を追加
            if (_currentRatingFilter > 0)
            {
                statusItems.Add($"{FindResource("String_Status_RatingFilter")}: {_currentRatingFilter}+");
            }

            // タグフィルタが有効な場合、その情報を追加
            if (_isTagFilterEnabled && _tagFilters.Count > 0)
            {
                string tagFilterText = string.Join(", ", _tagFilters);
                statusItems.Add($"{FindResource("String_Status_TagFilter")}: {tagFilterText}");
            }

            Dispatcher.Invoke(() =>
            {
                // プロンプトフィルタが有効な場合、その情報を追加
                if (_isPromptFilterEnabled)
                {
                    statusItems.Add($"{FindResource("String_Status_PromptFilter")}: {FindResource("String_Status_Enabled")}");
                }

                // ステータスバーに表示
                StatusBar.Text = string.Join(" | ", statusItems);
            });
        }

        private void OnSortOrderChanged(SortOrderChangedEventArgs args)
        {
            // ソート順の変更を処理
            _sortByDate = args.IsByDate;
            _sortAscending = args.IsAscending;

            // メニュー項目の状態を更新
            SortByDateMenuItem.IsChecked = _sortByDate;
            SortByNameMenuItem.IsChecked = !_sortByDate;
            SortAscendingMenuItem.IsChecked = _sortAscending;
            SortDescendingMenuItem.IsChecked = !_sortAscending;

            // 設定を保存
            _appSettings.SortByDate = _sortByDate;
            _appSettings.SortAscending = _sortAscending;
            SettingsHelper.SaveSettings(_appSettings);
        }
    }
}
