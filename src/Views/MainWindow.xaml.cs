using Illustra.Shared.Models.Tools; // Added for McpOpenFolderEventArgs
using System.IO;
using System.Windows;
using System.Windows.Controls; // Add this for MenuItem
using System.Windows.Controls.Primitives; // Add this for Thumb
using System.Windows.Input;   // Add this for KeyEventArgs
using Illustra.Helpers;
using Illustra.Functions;     // Add this for FuncId
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Illustra.Events;
using System.Threading.Tasks;
using Dragablz; // DragablzItem を使用するために追加
using System;
using Illustra.ViewModels;
using System.Collections.Generic;
using DryIoc.ImTools;
using Illustra.Models;
using MahApps.Metro.Controls;
using System.Windows.Media;
using Illustra.Shared.Models; // Added for MCP events

using System.Linq;
using System.Collections.Generic; // For List<string> if needed later
namespace Illustra.Views
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// このクラスはpartialクラスとして実装されており、プロパティの定義はMainWindow.Properties.csに分離されています。
    /// </summary>
    public partial class MainWindow : MetroWindow, INotifyPropertyChanged
    {
        private readonly IEventAggregator _eventAggregator;
        private AppSettingsModel _appSettings;
        private bool _sortByDate = true;
        private bool _sortAscending = true;
        private double _mainSplitterPosition;
        private double _favoritesFoldersSplitterPosition;
        private double _lastPropertyPanelHeight = 200;  // プロパティパネルのデフォルト高さ
        private FavoriteFoldersControl? _favoriteFoldersControl;
        private FolderTreeControl? _folderTreeControl;
        private string _currentFolderPath = string.Empty;
        private int _selectedItemCount = 0;
        private const string CONTROL_ID = "MainWindow";
        public bool EnableCyclicNavigation => App.Instance.EnableCyclicNavigation;

        // ソートメニューアイテム
        private MenuItem? _sortByDateAscendingMenuItem;
        private MenuItem? _sortByDateDescendingMenuItem;
        private MenuItem? _sortByNameAscendingMenuItem;
        private List<string> _currentExtensionFilters = new List<string>();
        private bool _isExtensionFilterEnabled = false;
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
            // ViewModelにDialogCoordinatorを設定
            _viewModel.MahAppsDialogCoordinator = MahApps.Metro.Controls.Dialogs.DialogCoordinator.Instance;
            this.OverlayBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#99000000")); // ダイアログのオーバーレイ色

            // 設定の読み込み
            _appSettings = SettingsHelper.GetSettings();

            // イベント購読
            // McpOpenFolderEvent の購読は ViewModel で行うため削除
            // FilterChangedEvent, SortOrderChangedEvent, ShortcutSettingsChangedEvent, SelectionCountChangedEvent の購読はそのまま
            _eventAggregator.GetEvent<FilterChangedEvent>().Subscribe(OnFilterChanged, ThreadOption.UIThread); // このイベントは自分自身のイベントも拾う設計
            _eventAggregator.GetEvent<SortOrderChangedEvent>().Subscribe(OnSortOrderChanged, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視
            _eventAggregator.GetEvent<ShortcutSettingsChangedEvent>().Subscribe(UpdateEditMenuShortcuts); // ショートカット変更イベントを購読
            _eventAggregator.GetEvent<SelectionCountChangedEvent>().Subscribe(OnSelectionCountChanged, ThreadOption.UIThread);
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
            Title = $"{(string)FindResource("String_AppName")} {versionString}";

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
            _viewModel.PropertyChanged += ViewModel_PropertyChanged; // SelectedTab の変更を監視
        }

        private void UpdateEditMenuShortcuts()
        {
            var shortcutHandler = KeyboardShortcutHandler.Instance;
            CopyMenuItem.InputGestureText = shortcutHandler.GetShortcutText(FuncId.Copy);
            PasteMenuItem.InputGestureText = shortcutHandler.GetShortcutText(FuncId.Paste);
            SelectAllMenuItem.InputGestureText = shortcutHandler.GetShortcutText(FuncId.SelectAll);
        }


        protected void MainWindow_OnKeyDown(object sender, KeyEventArgs e)
        {
            var shortcutHandler = KeyboardShortcutHandler.Instance;
            if (shortcutHandler.IsShortcutMatch(FuncId.TogglePropertyPanel, e.Key))
            {
                TogglePropertyPanel();
                e.Handled = true;
            }
        }

        private void TogglePropertyPanel()
        {
            // プロパティパネルとスプリッターの現在の状態を取得
            var isVisible = RightPanelGrid.RowDefinitions[2].Height.Value > 0;

            if (isVisible)
            {
                // 非表示にする前に現在の高さを保存
                _lastPropertyPanelHeight = RightPanelGrid.RowDefinitions[2].ActualHeight;

                // パネルとスプリッターを非表示に
                RightPanelGrid.RowDefinitions[1].Height = new GridLength(0);
                RightPanelGrid.RowDefinitions[2].Height = new GridLength(0);
            }
            else
            {
                // パネルとスプリッターを表示
                RightPanelGrid.RowDefinitions[1].Height = new GridLength(3);
                RightPanelGrid.RowDefinitions[2].Height = new GridLength(_lastPropertyPanelHeight);
            }

            // 設定を保存
            _appSettings.MainPropertyPanelVisible = !isVisible;
            _appSettings.PropertySplitterPosition = _lastPropertyPanelHeight;
            SettingsHelper.SaveSettings(_appSettings);
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
            // 内部状態は OnFilterChanged で更新されるので、ここではイベント発行のみ
            // IsTagFilterEnabled = false;
            // _tagFilters.Clear();
            // _currentRatingFilter = 0;
            // IsPromptFilterEnabled = false;
            // _currentExtensionFilters.Clear();
            // _isExtensionFilterEnabled = false;

            // フィルタ変更イベントを発行（クリア操作）
            _eventAggregator.GetEvent<FilterChangedEvent>().Publish(
                new FilterChangedEventArgsBuilder(CONTROL_ID).SetClear().Build()); // Clear() -> SetClear() に修正

            // UI更新は OnFilterChanged 内で行われる
            // UpdateFilterMenu();
            // UpdateStatusBar();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 編集メニューのショートカットキー表示を更新
            UpdateEditMenuShortcuts();

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

            // ViewModel に初期タブ状態のイベント発行を指示 (これは ThumbnailList など他のコントロールが初期状態を知るために必要)
            _viewModel.PublishInitialTabState();

            // 起動時のタブ選択処理を実行
            SelectInitialTab();

            // TabablzControl のインスタンスを取得して ViewModel に設定
            var tabControl = TabablzControl.GetLoadedInstances().FirstOrDefault();
            if (_viewModel != null && tabControl != null)
            {
                 _viewModel.TabControl = tabControl;
                 Debug.WriteLine("[MainWindow_Loaded] TabControl instance set in ViewModel.");
            }
            else
            {
                Debug.WriteLine("[MainWindow_Loaded] Error: Could not find TabablzControl instance or ViewModel is null.");
            }
        }

        /// <summary>
        /// 起動時に復元されたタブの中から、設定に基づいて初期選択タブを設定します。
        /// </summary>
        private void SelectInitialTab()
        {
            var settings = SettingsHelper.GetSettings();
            TabViewModel? tabToSelect = null;

            // 最後に開いていたタブを復元する場合
            if (settings.StartupMode == AppSettingsModel.StartupFolderMode.LastOpened && _viewModel.Tabs.Count > 0)
            {
                if (settings.LastActiveTabIndex >= 0 && settings.LastActiveTabIndex < _viewModel.Tabs.Count)
                {
                    // タブの選択をUIスレッドで行う
                    tabToSelect = _viewModel.Tabs[settings.LastActiveTabIndex];
                }
            }

            // 選択すべきタブが見つからない、または他のモードの場合は最初のタブを選択 (タブが存在する場合)
            if (tabToSelect == null && _viewModel.Tabs.Count > 0)
            {
                tabToSelect = _viewModel.Tabs[0];
            }

            // 実際にタブを選択 (View側で実行)
            if (tabToSelect != null)
            {
                // 初回の Tabs 設定時は SelectedTab に関係なく最初のタブが選択されるため、ここで上書きする
                _viewModel.SelectedTab = tabToSelect;

                // 復元したアクティブパスが開かれたことを通知
                _eventAggregator.GetEvent<McpOpenFolderEvent>().Publish(
                    new McpOpenFolderEventArgs()
                    {
                        FolderPath = tabToSelect.State.FolderPath,
                        SourceId = CONTROL_ID // 自分のIDを設定
                    });
            }
            else
            {
                Debug.WriteLine("[SelectInitialTab] No tab to select.");
            }
        }

        // Removed methods related to MainTabControl.Loaded and ItemContainerGenerator.StatusChanged
        // Using ItemContainerStyle.Loaded event instead.

        /// <summary>
        /// 指定された型の最初のビジュアル子要素を検索します。(TabItem_MouseRightButtonUpで使用)
        /// </summary>
        public static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T t)
                {
                    return t;
                }
                else
                {
                    T? childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                    {
                        return childOfChild;
                    }
                }
            }
            return null;
        }


        // DragablzItem_Loaded メソッドは ItemContainerStyle で EventSetter を使うため不要になりました。

        /// <summary>
        /// タブアイテムが右クリックされたときにコンテキストメニューを表示する
        /// </summary>
        private void TabItem_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            DragablzItem? tabItem = null;
            // イベントソースがThumbかDragablzItemかを確認
            if (sender is Thumb thumb)
            {
                // Thumbから親のDragablzItemを探す
                DependencyObject parent = VisualTreeHelper.GetParent(thumb);
                while (parent != null && !(parent is DragablzItem))
                {
                    parent = VisualTreeHelper.GetParent(parent);
                }
                tabItem = parent as DragablzItem;
            }
            else if (sender is DragablzItem item) // TextBlockからのイベントは削除したので、これは不要かもしれないが念のため残す
            {
                tabItem = item;
            }

            if (tabItem != null && tabItem.DataContext is TabViewModel tabViewModel && DataContext is MainWindowViewModel mainWindowViewModel)
            {
                var contextMenu = new ContextMenu();
                // 複製
                var duplicateItem = new MenuItem
                {
                    Header = FindResource("String_Tab_Duplicate"), // リソースから取得
                    Command = mainWindowViewModel.DuplicateTabCommand,
                    CommandParameter = tabViewModel
                };
                duplicateItem.IsEnabled = mainWindowViewModel.DuplicateTabCommand.CanExecute(tabViewModel);
                contextMenu.Items.Add(duplicateItem);

                // 閉じる
                var closeItem = new MenuItem
                {
                    Header = FindResource("String_Tab_Close"), // リソースから取得
                    Command = mainWindowViewModel.CloseTabCommand,
                    CommandParameter = tabViewModel
                };
                closeItem.IsEnabled = mainWindowViewModel.CloseTabCommand.CanExecute(tabViewModel);
                contextMenu.Items.Add(closeItem);

                // 他のタブを閉じる
                var closeOthersItem = new MenuItem
                {
                    Header = FindResource("String_Tab_CloseOthers"), // リソースから取得
                    Command = mainWindowViewModel.CloseOtherTabsCommand,
                    CommandParameter = tabViewModel
                };
                closeOthersItem.IsEnabled = mainWindowViewModel.CloseOtherTabsCommand.CanExecute(tabViewModel);
                contextMenu.Items.Add(closeOthersItem);

                // ContextMenuService を使って表示
                ContextMenuService.SetContextMenu(tabItem, contextMenu);
                contextMenu.IsOpen = true;

                e.Handled = true; // イベントの伝播を停止してデフォルトメニューを防ぐ
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
                // プロパティパネルの状態を保存
                _appSettings.MainPropertyPanelVisible = RightPanelGrid.RowDefinitions[2].Height.Value > 0;
                if (_appSettings.MainPropertyPanelVisible)
                {
                    _appSettings.PropertySplitterPosition = RightPanelGrid.RowDefinitions[2].ActualHeight;
                }
                else
                {
                    _appSettings.PropertySplitterPosition = _lastPropertyPanelHeight;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"スプリッター位置の保存に失敗: {ex.Message}");
            }

            FavoriteFolders.SetCurrentSettings();
            FolderTree.SetCurrentSettings();
            ThumbnailList.SetCurrentSettings();

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
            // 内部状態は OnFilterChanged で更新されるので、ここではイベント発行のみ
            bool newPromptFilterState = !IsPromptFilterEnabled; // 先に新しい状態を計算
            // IsPromptFilterEnabled = newPromptFilterState;
            // UpdateFilterMenu(); // OnFilterChanged で呼ばれる

            // フィルタ変更イベントを発行
            _eventAggregator.GetEvent<FilterChangedEvent>().Publish(
                new FilterChangedEventArgsBuilder(CONTROL_ID)
                .WithPromptFilter(newPromptFilterState).Build()); // 更新後の状態を渡す
            // UpdateStatusBar(); // OnFilterChanged で呼ばれる
        }

        private void FilterTagMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // 複数タグに対応したコンストラクタを使用
            var dialog = new TagFilterDialog(new List<string>(_tagFilters));
            if (dialog.ShowDialog() == true)
            {
                // 内部状態は OnFilterChanged で更新されるので、ここではイベント発行のみ
                bool newIsTagFilterEnabled = dialog.TagFilters.Count > 0;
                var newTagFilters = new List<string>(dialog.TagFilters);
                // IsTagFilterEnabled = newIsTagFilterEnabled;
                // _tagFilters = newTagFilters;
                // UpdateFilterMenu(); // OnFilterChanged で呼ばれる

                // フィルタ変更イベントを発行（新しいリストのインスタンスを作成）
                _eventAggregator.GetEvent<FilterChangedEvent>().Publish(
                    new FilterChangedEventArgsBuilder(CONTROL_ID)
                    .WithTagFilter(newIsTagFilterEnabled, newTagFilters).Build()); // 更新後の状態を渡す
            }
            // UpdateStatusBar(); // OnFilterChanged で呼ばれる
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
            int newRatingFilter = (rating == _currentRatingFilter && rating > 0) ? 0 : rating;
            // _currentRatingFilter = newRatingFilter;
            // UpdateFilterMenu(); // OnFilterChanged で呼ばれる

            // フィルタ変更イベントを発行
            _eventAggregator.GetEvent<FilterChangedEvent>().Publish(
                new FilterChangedEventArgsBuilder(CONTROL_ID)
                .WithRatingFilter(newRatingFilter).Build()); // 更新後の状態を渡す
            // UpdateStatusBar(); // OnFilterChanged で呼ばれる
        }

        private void FilterExtensionSubMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not string tag)
                return;

            var extensionsToToggle = tag.Split(',').ToList();
            bool isChecked = menuItem.IsChecked;

            if (isChecked)
            {
                // チェックされた場合、フィルタに追加
                foreach (var ext in extensionsToToggle)
                {
                    if (!_currentExtensionFilters.Contains(ext))
                    {
                        _currentExtensionFilters.Add(ext);
                    }
                }
            }
            else
            {
                // チェックが外れた場合、フィルタから削除
                foreach (var ext in extensionsToToggle)
                {
                    _currentExtensionFilters.Remove(ext);
                }
            }

            // 内部状態は OnFilterChanged で更新されるので、ここではイベント発行のみ
            bool newIsExtensionFilterEnabled = _currentExtensionFilters.Any();
            var newExtensionFilters = new List<string>(_currentExtensionFilters); // コピーを作成
            // _isExtensionFilterEnabled = newIsExtensionFilterEnabled;
            // UpdateFilterMenu(); // OnFilterChanged で呼ばれる

            // フィルタ変更イベントを発行
            _eventAggregator.GetEvent<FilterChangedEvent>().Publish(
                new FilterChangedEventArgsBuilder(CONTROL_ID)
                .WithExtensionFilter(newIsExtensionFilterEnabled, newExtensionFilters).Build()); // 更新後の状態を渡す

            // UpdateStatusBar(); // OnFilterChanged で呼ばれる
        }


        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void ShowAboutDialog(object sender, RoutedEventArgs e)
        {
            var dialog = new VersionInfoDialog();
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

            // 新規ファイル自動選択の設定を適用 (ViewModelに移動)
            // ToggleAutoSelectNewFile.IsChecked = _viewModel.IsAutoSelectNewFileEnabled; // ViewModelのプロパティにバインドされているため不要
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

                // 現在の高さを保存
                _lastPropertyPanelHeight = _appSettings.PropertySplitterPosition;
                if (!_appSettings.MainPropertyPanelVisible)
                {
                    // パネルとスプリッターを非表示に
                    RightPanelGrid.RowDefinitions[1].Height = new GridLength(0);
                    RightPanelGrid.RowDefinitions[2].Height = new GridLength(0);
                }
                else
                {
                    // パネルとスプリッターを表示
                    RightPanelGrid.RowDefinitions[1].Height = new GridLength(3);
                    RightPanelGrid.RowDefinitions[2].Height = new GridLength(_lastPropertyPanelHeight, GridUnitType.Pixel);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"スプリッター位置の復元に失敗: {ex.Message}");
            }
        }

        private void OnFilterChanged(FilterChangedEventArgs args)
        {
            Debug.WriteLine($"[MainWindow.OnFilterChanged] Received from: {args.SourceId}, Clear: {args.IsClearOperation}, Changed: {string.Join(",", args.ChangedTypes)}"); // IsFullUpdate 削除

            // クリア操作の場合
            if (args.IsClearOperation)
            {
                _currentRatingFilter = 0;
                _isPromptFilterEnabled = false;
                _isTagFilterEnabled = false;
                _tagFilters.Clear();
                _isExtensionFilterEnabled = false;
                _currentExtensionFilters.Clear();
            }
            // 個別の変更操作の場合
            else
            {
                if (args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Rating))
                    _currentRatingFilter = args.RatingFilter;
                if (args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Prompt))
                    _isPromptFilterEnabled = args.IsPromptFilterEnabled;
                if (args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Tag))
                {
                    _isTagFilterEnabled = args.IsTagFilterEnabled;
                    _tagFilters = new List<string>(args.TagFilters ?? new List<string>());
                }
                if (args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Extension))
                {
                    _isExtensionFilterEnabled = args.IsExtensionFilterEnabled;
                    _currentExtensionFilters = new List<string>(args.ExtensionFilters ?? new List<string>());
                }
            }

            // UIを更新
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

            // フィルタクリアメニューの有効/無効を更新
            FilterClearMenuItem.IsEnabled = _currentRatingFilter > 0 || _isPromptFilterEnabled || _isTagFilterEnabled || _isExtensionFilterEnabled;

            // 拡張子フィルタのサブメニュー項目の状態を更新
            foreach (var item in FilterExtensionMenuItem.Items.OfType<MenuItem>())
            {
                if (item.Tag is string tag)
                {
                    var extensionsInTag = tag.Split(',').ToList();
                    // タグ内のすべての拡張子が現在のフィルタに含まれている場合にチェックを入れる
                    item.IsChecked = extensionsInTag.All(ext => _currentExtensionFilters.Contains(ext));
                }
            }
            // 親メニューのチェック状態も更新 (いずれかの子がチェックされていれば親もチェックされているように見せる - IsCheckable=Falseなので見た目だけ)
            FilterExtensionMenuItem.IsChecked = FilterExtensionMenuItem.Items.OfType<MenuItem>().Any(mi => mi.IsChecked);
        }

        private void OnMcpFolderSelected(McpOpenFolderEventArgs args) // Renamed and changed args type, removed async
        {
            // 現在のフォルダパスを更新
            _currentFolderPath = args.FolderPath; // Changed property name

            // フィルタをクリア
            _currentRatingFilter = 0;
            _isPromptFilterEnabled = false;
            _tagFilters = new List<string>();
            _isTagFilterEnabled = false;
            _currentExtensionFilters.Clear(); // 追加
            _isExtensionFilterEnabled = false; // 追加

            // メニューの状態を更新
            UpdateFilterMenu();

            // フィルタクリアイベントを発行
            FilterChangedEventArgs filterArgs = new FilterChangedEventArgsBuilder(CONTROL_ID)
                .SetClear().Build(); // Clear() -> SetClear() に修正
            OnFilterChanged(filterArgs);

            // ステータスバーを更新
            _selectedItemCount = 0; // フォルダが変わったら選択数をリセット
            UpdateStatusBar();
        }

        private void OnDateSortChanged(object sender, RoutedEventArgs e) // CS1998 Fix: Removed unnecessary async
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

        private void OnNameSortChanged(object sender, RoutedEventArgs e)
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

        private void OnSelectionCountChanged(SelectionCountChangedEventArgs args)
        {
            _selectedItemCount = args.SelectedCount;
            UpdateStatusBar();
        }


        /// <summary>
        /// ステータスバーを更新します
        /// </summary>
        private void UpdateStatusBar()
        {
            try
            {
                var statusParts = new List<string>();

                // 現在のフォルダパス
                if (!string.IsNullOrEmpty(_currentFolderPath))
                {
                    statusParts.Add(_currentFolderPath);
                }

                // 選択中のアイテム数
                if (_selectedItemCount > 0)
                {
                    // リソース文字列 String_Status_SelectedItemsFormat を使用
                    statusParts.Add(string.Format((string)FindResource("String_Status_SelectedItemsFormat") ?? "{0} items selected", _selectedItemCount));
                }


                // フィルタ情報
                var filterDescriptions = new List<string>();
                if (IsPromptFilterEnabled)
                {
                    filterDescriptions.Add((string)FindResource("String_Status_Filter_Prompt"));
                }
                if (IsTagFilterEnabled && _tagFilters.Any())
                {
                    filterDescriptions.Add(string.Format((string)FindResource("String_Status_Filter_Tag"), string.Join(", ", _tagFilters)));
                }
                if (_currentRatingFilter > 0)
                {
                    filterDescriptions.Add(string.Format((string)FindResource("String_Status_Filter_Rating"), _currentRatingFilter));
                }
                if (_isExtensionFilterEnabled && _currentExtensionFilters.Any()) // 追加
                {
                    filterDescriptions.Add(string.Format((string)FindResource("String_Status_Filter_Extension"), string.Join(", ", _currentExtensionFilters)));
                }

                if (filterDescriptions.Any())
                {
                    statusParts.Add($"[{string.Join(" | ", filterDescriptions)}]");
                }

                // ステータスバーに表示
                StatusTextBlock.Text = string.Join("  ", statusParts);
            }
            catch (Exception ex)
            {
                // エラーログを出力
                LogHelper.LogError($"ステータスバーの更新中にエラーが発生しました: {ex.Message}", ex);

                // 開発者モードが有効な場合はステータスバーにエラーメッセージを表示
                if (SettingsHelper.GetSettings().DeveloperMode)
                {
                    StatusTextBlock.Text = $"Error: {ex.Message}";
                }
            }
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

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedTab))
            {
                // 選択されたタブが変わったら現在のフォルダパスを更新
                _currentFolderPath = _viewModel.SelectedTab?.State.FolderPath ?? string.Empty;
                // ステータスバーを更新
                UpdateStatusBar();
            }
        }


        private void TabItem_DragEnter(object sender, DragEventArgs e)
        {
            var dragablzItem = sender as DragablzItem;
            if (dragablzItem != null)
            {
                DragDropHelper.SetIsDragHover(dragablzItem, true);
                var effect = (e.KeyStates & DragDropKeyStates.ControlKey) != 0 ?
                    DragDropEffects.Copy : DragDropEffects.Move;
                e.Effects = effect;
                e.Handled = true;
            }
        }

        private void TabItem_DragLeave(object sender, DragEventArgs e)
        {
            var dragablzItem = sender as DragablzItem;
            if (dragablzItem != null)
            {
                DragDropHelper.SetIsDragHover(dragablzItem, false);
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handles the DragOver event for a TabItem.
        /// Determines the allowed drop effect based on the dragged data and target tab.
        /// </summary>
        private void TabItem_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None; // Default to no drop

            if (sender is DragablzItem tabItem && tabItem.DataContext is TabViewModel tabViewModel)
            {
                // Check if the tab represents a valid folder
                if (!string.IsNullOrEmpty(tabViewModel.State?.FolderPath) && Directory.Exists(tabViewModel.State.FolderPath))
                {
                    // Check if the dragged data contains files
                    if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    {
                        // Check if Ctrl key is pressed for copy operation
                        bool isCopy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
                        e.Effects = isCopy ? DragDropEffects.Copy : DragDropEffects.Move;
                    }
                }
            }
            e.Handled = true;
        }

        /// <summary>
        /// Handles the Drop event for a TabItem.
        /// Processes the dropped files by delegating to the MainWindowViewModel.
        /// </summary>
        private async void TabItem_Drop(object sender, DragEventArgs e)
        {
            if (sender is DragablzItem tabItem && tabItem.DataContext is TabViewModel tabViewModel)
            {
                // Get the target folder path from the TabViewModel
                string? targetFolderPath = tabViewModel.State?.FolderPath;

                if (!string.IsNullOrEmpty(targetFolderPath) && Directory.Exists(targetFolderPath) && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    // Get the list of dropped files
                    if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                    {
                        // Determine if it's a copy or move operation
                        bool isCopy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;

                        // Activate the target tab before processing // Removed this line: _viewModel.SelectedTab = tabViewModel;

                        // Delegate the file processing to the ViewModel
                        // We'll add this method to MainWindowViewModel next
                        await _viewModel.ProcessDroppedFilesAsync(files, targetFolderPath, isCopy);

                        e.Handled = true;
                    }
                }
            }

            // If not handled, reset effects
            if (!e.Handled)
            {
                e.Effects = DragDropEffects.None;
            }
        }
    }
}
