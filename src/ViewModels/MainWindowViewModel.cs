using System.Windows;
using Prism.Commands;
using Prism.Mvvm;
using Illustra.Views;
using Illustra.Views.Settings;
using Illustra.Helpers;
using MahApps.Metro.Controls.Dialogs; // IDialogCoordinator を使うために追加
using System; // ArgumentNullException を使うために追加
using Prism.Events; // IEventAggregator を使うために追加
using Illustra.Events; // ShortcutSettingsChangedEvent を使うために追加
using System.Windows.Input; // ICommand を使うために追加
using Illustra.Models; // IllustraAppContext, TabViewModel を使うために追加
using System.Collections.ObjectModel; // ObservableCollection を使うために追加
using System.Threading.Tasks;
using Illustra.Shared.Models.Tools; // Task を使うために追加
using System.Linq; // Linq を使用するために追加
using System.Diagnostics; // Debug を使用するために追加
using System.IO; // Path クラスを使用するために追加
using System.Collections.Generic; // List を使うために追加
using System.Threading; // CancellationTokenSource を使うために追加
using MahApps.Metro.Controls; // MetroWindow を使うために追加 (DialogHelper用)
using Dragablz; // TabablzControl.AddItem を使うために追加

namespace Illustra.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private string _statusMessage = "";
        private bool _isLightTheme;
        private bool _isDarkTheme;
        private readonly IEventAggregator _eventAggregator;
        private readonly ThumbnailListViewModel _thumbnailListViewModel; // Renamed from _mainViewModel
        private readonly string CONTROL_ID = "MainWindowViewModel";
        private readonly FileOperationHelper _fileOperationHelper; // FileOperationHelper のフィールドを追加


        // TabablzControl のインスタンスを保持するためのプロパティ (Viewから設定される)
        public TabablzControl? TabControl { get; set; } // Nullable に変更
        // DialogCoordinator プロパティを追加
        public IDialogCoordinator MahAppsDialogCoordinator { get; set; }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsLightTheme
        {
            get => _isLightTheme;
            private set => SetProperty(ref _isLightTheme, value);
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            private set => SetProperty(ref _isDarkTheme, value);
        }

        // 新規ファイル自動選択設定
        public bool IsAutoSelectNewFileEnabled
        {
            get => SettingsHelper.GetSettings().AutoSelectNewFile;
            set
            {
                var settings = SettingsHelper.GetSettings();
                if (settings.AutoSelectNewFile != value)
                {
                    settings.AutoSelectNewFile = value;
                    SettingsHelper.SaveSettings(settings);
                    RaisePropertyChanged(nameof(IsAutoSelectNewFileEnabled)); // プロパティ変更を通知
                }
            }
        }
        private TabViewModel _selectedTab;

        /// <summary>
        /// 管理しているタブのコレクション
        /// </summary>
        public ObservableCollection<TabViewModel> Tabs { get; } = new ObservableCollection<TabViewModel>();

        /// <summary>
        /// 現在選択されているタブ
        /// </summary>
        public TabViewModel SelectedTab
        {
            get => _selectedTab;
            set // 通常の set に戻す
            {
                if (SetProperty(ref _selectedTab, value))
                {
                    // タブ切り替え時の処理 (ステップ7)
                    // EventAggregator を使用してイベントを発行
                    _eventAggregator?.GetEvent<SelectedTabChangedEvent>().Publish(
                        new SelectedTabChangedEventArgs(_selectedTab?.State));
                    _eventAggregator?.GetEvent<McpOpenFolderEvent>().Publish(
                        new McpOpenFolderEventArgs()
                        {
                            FolderPath = _selectedTab?.State?.FolderPath,
                            SelectedFilePath = _selectedTab?.State?.SelectedItemPath,
                            SourceId = CONTROL_ID // 追加: ソースIDを指定
                        });
                    // RaisePropertyChanged は SetProperty 内で呼ばれるため不要
                }
            }
        }

        /// <summary>
        /// 閉じるボタンを表示するかどうかを示す値。
        /// タブが2つ以上ある場合に true になります。
        /// </summary>
        public bool ShowCloseButton => Tabs.Count > 1;

        /// <summary>
        /// タブが1つ以上存在するかどうかを示す値。
        /// </summary>
        public bool HasTabs => Tabs.Count > 0;


        public DelegateCommand OpenLanguageSettingsCommand { get; }
        public DelegateCommand OpenShortcutSettingsCommand { get; }
        public DelegateCommand OpenAdvancedSettingsCommand { get; }
        public DelegateCommand OpenImageGenerationWindowCommand { get; }
        public DelegateCommand<TabViewModel> CloseTabCommand { get; private set; }
        public DelegateCommand<TabViewModel> CloseOtherTabsCommand { get; private set; }
        public DelegateCommand<TabViewModel> DuplicateTabCommand { get; private set; }

        public DelegateCommand AddNewTabCommand { get; private set; }

        // MainViewModel からコマンドを公開
        public ICommand CopyCommand => _thumbnailListViewModel.CopyCommand;
        public ICommand PasteCommand => _thumbnailListViewModel.PasteCommand;
        public ICommand SelectAllCommand => _thumbnailListViewModel.SelectAllCommand;


        /// <summary>
        /// サムネイルリストのViewModel
        /// </summary>
        public ThumbnailListViewModel ThumbnailListViewModel => _thumbnailListViewModel;

        public DelegateCommand SetLightThemeCommand { get; }
        public DelegateCommand SetDarkThemeCommand { get; }

        public MainWindowViewModel(IEventAggregator eventAggregator, ThumbnailListViewModel thumbnailListViewModel, FileOperationHelper fileOperationHelper) // 引数に FileOperationHelper, ThumbnailListViewModel を追加
        {
            // IllustraAppContext から MainViewModel を取得 (既存のコード)
            // var appContext = ContainerLocator.Container.Resolve<IllustraAppContext>(); // AppContextからの取得は不要に
            _thumbnailListViewModel = thumbnailListViewModel ?? throw new ArgumentNullException(nameof(thumbnailListViewModel)); // 引数から受け取るように変更

            // FileOperationHelper をフィールドに代入
            _fileOperationHelper = fileOperationHelper ?? throw new ArgumentNullException(nameof(fileOperationHelper));

            // IEventAggregator を解決してフィールドに設定 (既存のコード)
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator)); // 引数から受け取るように変更
            // McpOpenFolderEvent を購読 (ステップ8の接続)
            _eventAggregator.GetEvent<McpOpenFolderEvent>().Subscribe(OnMcpOpenFolderReceived, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID && filter.SourceId != "MainWindow");
            _eventAggregator.GetEvent<OpenInNewTabEvent>().Subscribe(args => HandleOpenInNewTab(args.FolderPath));
            _eventAggregator.GetEvent<FilterChangedEvent>().Subscribe(OnFilterChanged, ThreadOption.UIThread);
            _eventAggregator.GetEvent<SortOrderChangedEvent>().Subscribe(OnSortOrderChanged, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID);
            _eventAggregator.GetEvent<FavoriteDisplayNameChangedEvent>().Subscribe(OnFavoriteDisplayNameChanged, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID);


            _eventAggregator.GetEvent<FileSelectedEvent>().Subscribe(OnFileSelectedInTab, ThreadOption.UIThread);


            OpenLanguageSettingsCommand = new DelegateCommand(ExecuteOpenLanguageSettings);
            OpenShortcutSettingsCommand = new DelegateCommand(ExecuteOpenShortcutSettings);
            OpenAdvancedSettingsCommand = new DelegateCommand(ExecuteOpenAdvancedSettings);
            OpenImageGenerationWindowCommand = new DelegateCommand(ExecuteOpenImageGenerationWindow);
            SetLightThemeCommand = new DelegateCommand(ExecuteSetLightTheme);
            SetDarkThemeCommand = new DelegateCommand(ExecuteSetDarkTheme);

            // タブ操作コマンドの初期化
            CloseTabCommand = new DelegateCommand<TabViewModel>(ExecuteCloseTab, CanExecuteCloseTab);
            CloseOtherTabsCommand = new DelegateCommand<TabViewModel>(ExecuteCloseOtherTabs, CanExecuteCloseOtherTabs);
            DuplicateTabCommand = new DelegateCommand<TabViewModel>(ExecuteDuplicateTab, CanExecuteDuplicateTab);
            AddNewTabCommand = new DelegateCommand(ExecuteAddNewTab);


            // 現在のテーマを反映
            var settings = SettingsHelper.GetSettings();
            IsLightTheme = settings.Theme == "Light";
            IsDarkTheme = settings.Theme == "Dark";

            // ステータスメッセージの初期化
            StatusMessage = (string)Application.Current.Resources["String_Status_Ready"];
            // アプリケーション起動時に保存されたタブ状態を読み込む
            LoadTabStates();
            // 初期タブがない場合はデフォルトタブを追加

            // LoadTabStates でタブが復元された場合、最初のタブを選択して状態を適用
            if (SelectedTab == null && Tabs.Count > 0)
            {
                SelectedTab = Tabs[0]; // 最初のタブを選択
                                       // SelectedTab のセッター内でイベントが発行される
            }
            // タブコレクションの変更を監視して ShowCloseButton の変更通知を発行
            Tabs.CollectionChanged += (s, e) =>
            {
                RaisePropertyChanged(nameof(ShowCloseButton));
                RaisePropertyChanged(nameof(HasTabs)); // HasTabs の変更通知を追加
            };
        }
        // コンストラクタを削除 (新しいコンストラクタで置き換え)
        public MainWindowViewModel()
        {
            // IllustraAppContext から MainViewModel を取得
            var appContext = ContainerLocator.Container.Resolve<IllustraAppContext>();
            _thumbnailListViewModel = appContext?.MainViewModel ?? throw new InvalidOperationException("ThumbnailListViewModel (originally MainViewModel) could not be resolved from AppContext.");

            // IEventAggregator を解決してフィールドに設定
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            // McpOpenFolderEvent を購読 (ステップ8の接続)
            _eventAggregator.GetEvent<McpOpenFolderEvent>().Subscribe(OnMcpOpenFolderReceived, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID && filter.SourceId != "MainWindow"); // 自分以外からのイベントを受信するように変更
            // OpenInNewTabEvent を購読 (ステップ9の接続)
            _eventAggregator.GetEvent<OpenInNewTabEvent>().Subscribe(args => HandleOpenInNewTab(args.FolderPath));
            _eventAggregator.GetEvent<FilterChangedEvent>().Subscribe(OnFilterChanged, ThreadOption.UIThread);
            _eventAggregator.GetEvent<SortOrderChangedEvent>().Subscribe(OnSortOrderChanged, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // ソート順変更イベントを購読 (自分以外から)


            _eventAggregator.GetEvent<FileSelectedEvent>().Subscribe(OnFileSelectedInTab, ThreadOption.UIThread);


            OpenLanguageSettingsCommand = new DelegateCommand(ExecuteOpenLanguageSettings);
            OpenShortcutSettingsCommand = new DelegateCommand(ExecuteOpenShortcutSettings);
            OpenAdvancedSettingsCommand = new DelegateCommand(ExecuteOpenAdvancedSettings);
            OpenImageGenerationWindowCommand = new DelegateCommand(ExecuteOpenImageGenerationWindow);
            SetLightThemeCommand = new DelegateCommand(ExecuteSetLightTheme);
            SetDarkThemeCommand = new DelegateCommand(ExecuteSetDarkTheme);

            // タブ操作コマンドの初期化
            CloseTabCommand = new DelegateCommand<TabViewModel>(ExecuteCloseTab, CanExecuteCloseTab);
            CloseOtherTabsCommand = new DelegateCommand<TabViewModel>(ExecuteCloseOtherTabs, CanExecuteCloseOtherTabs);
            DuplicateTabCommand = new DelegateCommand<TabViewModel>(ExecuteDuplicateTab, CanExecuteDuplicateTab);
            AddNewTabCommand = new DelegateCommand(ExecuteAddNewTab);


            // 現在のテーマを反映
            var settings = SettingsHelper.GetSettings();
            IsLightTheme = settings.Theme == "Light";
            IsDarkTheme = settings.Theme == "Dark";

            // ステータスメッセージの初期化
            StatusMessage = (string)Application.Current.Resources["String_Status_Ready"];
            // アプリケーション起動時に保存されたタブ状態を読み込む
            LoadTabStates();
            // 初期タブがない場合はデフォルトタブを追加

            // LoadTabStates でタブが復元された場合、最初のタブを選択して状態を適用
            if (SelectedTab == null && Tabs.Count > 0)
            {
                SelectedTab = Tabs[0]; // 最初のタブを選択
                                       // SelectedTab のセッター内でイベントが発行される
            }
            // タブコレクションの変更を監視して ShowCloseButton の変更通知を発行
            Tabs.CollectionChanged += (s, e) =>
            {
                RaisePropertyChanged(nameof(ShowCloseButton));
                RaisePropertyChanged(nameof(HasTabs)); // HasTabs の変更通知を追加
            };
        }

        private void ExecuteOpenLanguageSettings()
        {
            // 言語設定画面をダイアログとして表示
            var languageSettingsWindow = new LanguageSettingsWindow
            {
                Owner = Application.Current.MainWindow
            };
            languageSettingsWindow.ShowDialog();
            StatusMessage = (string)Application.Current.Resources["String_Status_Ready"];
        }

        private void ExecuteOpenShortcutSettings()
        {
            // キーボードショートカット設定画面をダイアログとして表示
            var shortcutSettingsWindow = new KeyboardShortcutSettingsWindow
            {
                Owner = Application.Current.MainWindow
            };
            shortcutSettingsWindow.ShowDialog();
            _eventAggregator.GetEvent<ShortcutSettingsChangedEvent>().Publish(); // イベントを発行 (ShowDialogの後)
            StatusMessage = (string)Application.Current.Resources["String_Status_Ready"];
        }

        private void ExecuteOpenAdvancedSettings()
        {
            // 詳細設定画面をダイアログとして表示
            var advancedSettingsWindow = new Views.Settings.AdvancedSettingsWindow
            {
                Owner = Application.Current.MainWindow
            };

            if (advancedSettingsWindow.ShowDialog() == true)
            {
                StatusMessage = (string)Application.Current.Resources["String_Settings_SaveCompleted"];
            }
            else
            {
                StatusMessage = (string)Application.Current.Resources["String_Status_Ready"];
            }
        }

        private void ExecuteOpenImageGenerationWindow()
        {
            // 画像生成画面をモードレスダイアログとして表示
            var imageGenerationWindow = new ImageGenerationWindow
            {
                Owner = Application.Current.MainWindow
            };
            imageGenerationWindow.Show();
        }

        private void ExecuteSetLightTheme()
        {
            // ライトテーマに切り替え
            ((App)Application.Current).ChangeTheme("Light");
            StatusMessage = "ライトテーマに切り替えました";

            // プロパティを更新
            IsLightTheme = true;
            IsDarkTheme = false;

            // 設定を保存
            var settings = SettingsHelper.GetSettings();
            settings.Theme = "Light";
            SettingsHelper.SaveSettings(settings);
        }

        private void ExecuteSetDarkTheme()
        {
            // ダークテーマに切り替え
            ((App)Application.Current).ChangeTheme("Dark");
            StatusMessage = "ダークテーマに切り替えました";

            // プロパティを更新
            IsLightTheme = false;
            IsDarkTheme = true;

            // 設定を保存
            var settings = SettingsHelper.GetSettings();
            settings.Theme = "Dark";
            SettingsHelper.SaveSettings(settings);
        }

        // --- タブ操作コマンドの実行ロジック ---
        private void ExecuteCloseTab(TabViewModel tabToClose)
        {
            if (tabToClose == null || !Tabs.Contains(tabToClose)) return;

            int closingTabIndex = Tabs.IndexOf(tabToClose);
            Tabs.Remove(tabToClose);

            // 削除後に選択するタブを決定
            if (Tabs.Count > 0)
            {
                // 削除されたタブの前のタブを選択（ただし、最初のタブが削除された場合は新しい最初のタブを選択）
                int indexToSelect = Math.Max(0, closingTabIndex - 1);
                if (indexToSelect >= Tabs.Count) // 最後のタブが削除された場合
                {
                    indexToSelect = Tabs.Count - 1;
                }
                SelectedTab = Tabs[indexToSelect];
            }
            else
            {
                SelectedTab = null; // すべてのタブが閉じられた場合
                // 必要であれば新しいデフォルトタブを追加するなどの処理
                // AddNewTab(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            }

            // コマンドの実行可能状態を更新
            CloseTabCommand.RaiseCanExecuteChanged();
            CloseOtherTabsCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(ShowCloseButton)); // タブ数変更後に通知
        }
        private bool CanExecuteCloseTab(TabViewModel tabToClose) => tabToClose != null && Tabs.Count > 1; // 最後のタブは閉じられない

        private void ExecuteCloseOtherTabs(TabViewModel tabToKeep)
        {
            if (tabToKeep == null || !Tabs.Contains(tabToKeep)) return;

            var tabsToRemove = Tabs.Where(t => t != tabToKeep).ToList();
            foreach (var tab in tabsToRemove)
            {
                Tabs.Remove(tab);
            }
            SelectedTab = tabToKeep; // 保持するタブを選択状態にする

            // コマンドの実行可能状態を更新
            CloseTabCommand.RaiseCanExecuteChanged();
            CloseOtherTabsCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(ShowCloseButton)); // タブ数変更後に通知
        }
        private bool CanExecuteCloseOtherTabs(TabViewModel tabToKeep) => tabToKeep != null && Tabs.Count > 1;

        private void ExecuteDuplicateTab(TabViewModel tabToDuplicate)
        {
            if (tabToDuplicate?.State == null) return;

            // TabState をディープコピー
            var newState = tabToDuplicate.State.Clone();
            var newTab = new TabViewModel(newState);

            // Dragablz の静的メソッドを使ってアイテムを追加し、表示位置を指定する
            // このメソッドは内部でソースコレクションへの追加と、DragablzItemsControl への配置指示を行う
            Dragablz.TabablzControl.AddItem(newTab, tabToDuplicate, AddLocationHint.After);

            SelectedTab = newTab; // 新しいタブを選択

            // コマンドの実行可能状態を更新
            CloseTabCommand.RaiseCanExecuteChanged();
            CloseOtherTabsCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(ShowCloseButton)); // タブ数変更後に通知
        }
        private bool CanExecuteDuplicateTab(TabViewModel tabToDuplicate) => tabToDuplicate != null;


        // --- 新規タブ追加コマンド ---
        private void ExecuteAddNewTab()
        {
            var settings = SettingsHelper.GetSettings();
            string pathToAdd;

            if (settings.NewTabFolderModeSetting == AppSettingsModel.NewTabFolderMode.Specified &&
                !string.IsNullOrEmpty(settings.NewTabFolderPath) &&
                Directory.Exists(settings.NewTabFolderPath)) // フォルダが存在するか確認
            {
                pathToAdd = settings.NewTabFolderPath;
            }
            else
            {
                // 指定フォルダが無効、またはマイピクチャが選択されている場合はマイピクチャを使用
                pathToAdd = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                // マイピクチャが存在しない場合のフォールバック（レアケースだが念のため）
                if (!Directory.Exists(pathToAdd))
                {
                    Debug.WriteLine($"Warning: Default folder path '{pathToAdd}' not found. Falling back to MyDocuments.");
                    pathToAdd = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    // MyDocumentsも存在しない場合はさらにフォールバックが必要かもしれない
                    if (!Directory.Exists(pathToAdd))
                    {
                        Debug.WriteLine($"Warning: Fallback folder path '{pathToAdd}' also not found.");
                        // ここでエラー表示や、タブを開かないなどの処理も検討できる
                        // 今回はそのまま（存在しないかもしれない）パスを渡す
                    }
                }
            }

            AddNewTab(pathToAdd);
        }

        // --- タブ追加ヘルパーメソッド ---
        private void AddNewTab(string folderPath, string filePath = null)
        {
            var newState = new TabState { FolderPath = folderPath, SelectedItemPath = filePath };
            // FilterSettings と SortSettings はデフォルト値を使用
            newState.FilterSettings = new FilterSettings();
            newState.SortSettings = new SortSettings();
            var newTab = new TabViewModel(newState);
            Tabs.Add(newTab);
            SelectedTab = newTab; // 新しいタブを選択状態にする
                                  // AddNewTab は DuplicateTab からも呼ばれるため、ここで通知
            RaisePropertyChanged(nameof(ShowCloseButton));
        }

        /// <summary>
        /// アプリケーション終了時にタブ状態を保存する処理
        /// </summary>
        public void SaveTabStates()
        {
            var settings = SettingsHelper.GetSettings();

            // TabControl が View から設定されていれば、表示順序で TabState を取得
            if (TabControl != null)
            {
                try
                {
                    // GetOrderedHeaders は DragablzItem の IEnumerable を返す可能性が高い
                    var orderedItems = TabControl.GetOrderedHeaders()
                                                 .OfType<DragablzItem>() // DragablzItem 型でフィルター
                                                 .ToList();

                    // DragablzItem から TabViewModel を取得し、その State を保存
                    var orderedViewModels = orderedItems
                        .Select(item => item.Content as TabViewModel ?? item.DataContext as TabViewModel) // Content または DataContext から ViewModel を取得
                        .Where(vm => vm != null) // null チェック
                        .ToList();

                    settings.TabStates = orderedViewModels.Select(vm => vm.State).ToList();
                    // 最後にアクティブだったタブのインデックスも更新された順序に基づいて再計算
                    // orderedViewModels リスト内で SelectedTab のインデックスを探す
                    settings.LastActiveTabIndex = SelectedTab != null ? orderedViewModels.IndexOf(SelectedTab) : -1;
                    Debug.WriteLine($"[SaveTabStates] Saving tabs based on TabControl.GetOrderedHeaders ({orderedViewModels.Count} tabs). Last active index: {settings.LastActiveTabIndex}"); // 変数名を orderedViewModels に修正
                }
                catch (Exception ex)
                {
                     Debug.WriteLine($"[SaveTabStates] Error getting ordered headers: {ex.Message}. Falling back to collection order.");
                     // エラー発生時はフォールバック
                     settings.TabStates = Tabs.Select(vm => vm.State).ToList();
                     settings.LastActiveTabIndex = SelectedTab != null ? Tabs.IndexOf(SelectedTab) : -1;
                }
            }
            else
            {
                // TabControl が未設定の場合 (フォールバック) は現在のコレクション順で保存
                Debug.WriteLine("[SaveTabStates] Warning: TabControl instance not set in ViewModel. Saving tabs in current collection order.");
                settings.TabStates = Tabs.Select(vm => vm.State).ToList();
                // 最後にアクティブだったタブのインデックスを保存 (元のロジック)
                settings.LastActiveTabIndex = SelectedTab != null ? Tabs.IndexOf(SelectedTab) : -1;
            }

            SettingsHelper.SaveSettings(settings);
        }

        /// <summary>
        /// アプリケーション起動時にタブ状態を復元する処理
        /// </summary>
        private void LoadTabStates()
        {
            var settings = SettingsHelper.GetSettings();
            // StartupFolderMode が None または Specified の場合はタブを復元しない
            if (settings.StartupMode == AppSettingsModel.StartupFolderMode.None || settings.StartupMode == AppSettingsModel.StartupFolderMode.Specified)
            {
                return;
            }
            if (settings.TabStates != null && settings.TabStates.Any())
            {
                Tabs.Clear(); // 既存のタブ（もしあれば）をクリア
                foreach (var state in settings.TabStates)
                {
                    // SelectLastFileOnStartup が false の場合は、最後に選択したファイルのパスをクリアする
                    if (!settings.SelectLastFileOnStartup)
                    {
                        state.SelectedItemPath = null;
                    }

                    // 各状態から ViewModel を復元して追加
                    // 注意: FilterSettings や SortSettings が null の場合の考慮が必要かもしれない
                    var tabViewModel = new TabViewModel(state);
                    Tabs.Add(tabViewModel);
                }
                // 読み込み完了後にも通知
                RaisePropertyChanged(nameof(ShowCloseButton));
                // 最後にアクティブだったタブを選択する
                if (settings.LastActiveTabIndex >= 0 && settings.LastActiveTabIndex < Tabs.Count)
                {
                    SelectedTab = Tabs[settings.LastActiveTabIndex];
                }
                // 有効なインデックスがない場合は最初のタブを選択
                else if (Tabs.Count > 0)
                {
                    SelectedTab = Tabs[0];
                }
            }
        }
        /// <summary>
        /// フォルダツリーまたはお気に入りからフォルダが選択されたときの処理 (ステップ8)
        /// </summary>
        /// <param name="path">選択されたフォルダのパス</param>
        public void HandleFolderSelected(string path, string filePath)
        {
            if (string.IsNullOrEmpty(path)) return; // パスが無効な場合は何もしない

            // タブが0個の場合は新しいタブを作成して開く
            if (Tabs.Count == 0)
            {
                AddNewTab(path, filePath);
                return; // 新しいタブを作成したので以降の処理は不要
            }

            // アクティブなタブのパスを更新する (元の動作)
            if (SelectedTab == null || SelectedTab.State == null) return; // アクティブなタブまたはその状態がない場合は何もしない

            // アクティブなタブの状態を更新
            SelectedTab.State.FolderPath = path;
            SelectedTab.State.SelectedItemPath = filePath;

            // サムネイルリストに更新を通知
            // EventAggregator を使用してイベントを発行
            _eventAggregator?.GetEvent<SelectedTabChangedEvent>().Publish(
                new SelectedTabChangedEventArgs(SelectedTab.State));
        }

        /// <summary>
        /// フォルダツリーまたはお気に入りから「タブで開く」が要求されたときの処理 (ステップ9)
        /// </summary>
        /// <param name="path">開くフォルダのパス</param>
        public void HandleOpenInNewTab(string path)
        {
            if (string.IsNullOrEmpty(path)) return; // パスが無効な場合は何もしない

            // 既存のタブがない場合は、新しいタブを追加して選択状態にする
            AddNewTab(path);
        }

        /// <summary>
        /// McpOpenFolderEvent を受信したときの処理
        /// </summary>
        private void OnMcpOpenFolderReceived(McpOpenFolderEventArgs args)
        {
            HandleFolderSelected(args.FolderPath, args.SelectedFilePath);
        }

        /// <summary>
        /// MainWindow の Loaded イベント後に呼び出され、
        /// 初期選択タブの状態変更イベントを発行します。
        /// </summary>
        public void PublishInitialTabState()
        {
            // LoadTabStates で SelectedTab が設定されているはず
            if (_selectedTab != null)
            {
                System.Diagnostics.Debug.WriteLine($"[PublishInitialTabState] Publishing SelectedTabChangedEvent for: {_selectedTab.State?.FolderPath}");
                _eventAggregator?.GetEvent<SelectedTabChangedEvent>().Publish(
                    new SelectedTabChangedEventArgs(_selectedTab.State));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[PublishInitialTabState] SelectedTab is null, skipping event publish.");
            }
        }

        /// <summary>
        /// ThumbnailListControl から FileSelectedEvent を受信したときの処理。
        /// 現在のタブの SelectedItemPath を更新します。
        /// </summary>
        private void OnFileSelectedInTab(SelectedFileModel args)
        {
            // ThumbnailListControl からのイベントで、現在のタブの状態が存在する場合のみ処理
            if (args.SourceId == "ThumbnailList" && SelectedTab?.State != null)
            {
                // 選択されたファイルのパスが null または空でなく、
                // かつ、そのファイルが現在のタブのフォルダパスに属しているか確認
                if (!string.IsNullOrEmpty(args.FullPath) &&
                    Path.GetDirectoryName(args.FullPath)?.Equals(SelectedTab.State.FolderPath, StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Debug.WriteLine($"[OnFileSelectedInTab] Updating SelectedItemPath for Tab '{SelectedTab.State.FolderPath}' to: {args.FullPath}");
                    SelectedTab.State.SelectedItemPath = args.FullPath;
                }
                // 選択が解除された場合 (FullPath が null または空) や、フォルダが異なる場合は SelectedItemPath を null にする
                else if (string.IsNullOrEmpty(args.FullPath))
                {
                    // Debug.WriteLine($"[OnFileSelectedInTab] Clearing SelectedItemPath for Tab '{SelectedTab.State.FolderPath}' due to deselection.");
                    SelectedTab.State.SelectedItemPath = null;
                }
                // SaveTabStates はアプリ終了時に呼ばれるので、ここでは不要
            }
        }

        /// <summary>
        /// UI (MainWindowなど) から FilterChangedEvent を受信したときの処理。
        /// 現在のタブの FilterSettings を更新します。
        /// </summary>
        private void OnFilterChanged(FilterChangedEventArgs args)
        {
            // MainWindow からのイベントで、現在のタブの状態が存在する場合のみ処理
            // SourceId は MainWindow.xaml.cs で定義されている CONTROL_ID ("MainWindow") を想定
            // SourceId のチェックを削除し、どのソースからのイベントでも処理するように変更
            if (SelectedTab?.State != null)
            {
                var filterSettings = SelectedTab.State.FilterSettings;

                // クリア操作の場合
                if (args.IsClearOperation)
                {
                    if (!filterSettings.IsDefault)
                    {
                        filterSettings.Clear();
                        // Debug.WriteLine($"[OnFilterChanged-VM] Clearing Filters for Tab '{SelectedTab.State.FolderPath}'");
                    }
                    return; // クリアの場合は他の変更は無視
                }

                // 全更新操作の場合 (ViewModel側では特に何もしない、個別変更で対応)
                // IsFullUpdate の場合の特別な処理は不要になったため削除
                // FilterChangedEventArgsBuilder 側で ChangedTypes が適切に設定される

                // 個別のフィルタ変更を適用 (ChangedTypes リストを確認)
                if (args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Rating))
                {
                    if (filterSettings.Rating != args.RatingFilter)
                    {
                        filterSettings.Rating = args.RatingFilter;
                        // Debug.WriteLine($"[OnFilterChanged-VM] Updating Rating Filter for Tab '{SelectedTab.State.FolderPath}' to: {args.RatingFilter}");
                    }
                }
                if (args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Prompt))
                {
                    if (filterSettings.HasPrompt != args.IsPromptFilterEnabled)
                    {
                        filterSettings.HasPrompt = args.IsPromptFilterEnabled;
                        // Debug.WriteLine($"[OnFilterChanged-VM] Updating Prompt Filter for Tab '{SelectedTab.State.FolderPath}' to: {args.IsPromptFilterEnabled}");
                    }
                }
                if (args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Tag))
                {
                    var currentTags = new HashSet<string>(filterSettings.Tags ?? new List<string>());
                    var newTags = new HashSet<string>(args.TagFilters ?? new List<string>());
                    if (!currentTags.SetEquals(newTags))
                    {
                        filterSettings.Tags = new List<string>(newTags);
                        // Debug.WriteLine($"[OnFilterChanged-VM] Updating Tag Filter for Tab '{SelectedTab.State.FolderPath}'");
                    }
                }
                if (args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Extension))
                {
                    var currentExts = new HashSet<string>(filterSettings.Extensions ?? new List<string>());
                    var newExts = new HashSet<string>(args.ExtensionFilters ?? new List<string>());
                    if (!currentExts.SetEquals(newExts))
                    {
                        filterSettings.Extensions = new List<string>(newExts);
                        // Debug.WriteLine($"[OnFilterChanged-VM] Updating Extension Filter for Tab '{SelectedTab.State.FolderPath}'");
                    }
                }
            }
        }

        /// <summary>
        /// UI (MainWindowなど) から SortOrderChangedEvent を受信したときの処理。
        /// 現在のタブの SortSettings を更新します。
        /// </summary>
        private void OnSortOrderChanged(SortOrderChangedEventArgs args)
        {
            if (SelectedTab?.State?.SortSettings != null)
            {
                var sortSettings = SelectedTab.State.SortSettings;

                if (sortSettings.SortByDate != args.IsByDate)
                {
                    sortSettings.SortByDate = args.IsByDate;
                    // Debug.WriteLine($"[OnSortOrderChanged-VM] Updating SortByDate for Tab '{SelectedTab.State.FolderPath}' to: {args.IsByDate}");
                }

                if (sortSettings.SortAscending != args.IsAscending)
                {
                    sortSettings.SortAscending = args.IsAscending;
                    // Debug.WriteLine($"[OnSortOrderChanged-VM] Updating SortAscending for Tab '{SelectedTab.State.FolderPath}' to: {args.IsAscending}");
                }

                // SaveTabStates はアプリ終了時に呼ばれるので、ここでは不要
                // 必要であれば、ここで明示的に設定保存をトリガーすることも可能
            }
        }

        /// <summary>
        /// お気に入りフォルダの表示名が変更された時のイベントハンドラ
        /// </summary>
        private void OnFavoriteDisplayNameChanged(FavoriteDisplayNameChangedEventArgs args)
        {
            // 変更されたお気に入りフォルダを表示しているタブの表示名を更新
            foreach (var tab in Tabs.Where(t => t.State?.FolderPath == args.FolderPath))
            {
                tab.RefreshDisplayName();
            }
        }

        /// <summary>
        /// Processes files dropped onto a tab.
        /// </summary>
        /// <param name="files">List of dropped file paths.</param>
        /// <param name="targetFolderPath">The target folder path of the tab.</param>
        /// <param name="isCopy">True for copy operation, false for move.</param>
        public async Task ProcessDroppedFilesAsync(string[] files, string targetFolderPath, bool isCopy)
        {
            if (files == null || files.Length == 0 || string.IsNullOrEmpty(targetFolderPath))
            {
                return;
            }

            string dialogTitle = isCopy ?
                (string)Application.Current.FindResource("String_Dialog_FileCopyTitle") :
                (string)Application.Current.FindResource("String_Dialog_FileMoveTitle");

            var owner = Application.Current.MainWindow as MetroWindow;
            if (owner == null)
            {
                // Handle error: Owner window not found or not a MetroWindow
                Debug.WriteLine("Error: Owner window is not a MetroWindow.");
                // Optionally show a message box or log the error
                MessageBox.Show("Cannot perform file operation: Application window not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }


            var cts = new CancellationTokenSource();
            (IProgress<FileOperationProgressInfo> progress, Action closeDialog) = (null, null);

            try
            {
                // Show progress dialog
                (progress, closeDialog) = await DialogHelper.ShowProgressDialogAsync(owner, dialogTitle, cts);

                // Execute file operation in background
                var processedFiles = await Task.Run(async () =>
                {
                    try
                    {
                        // Define post-processing action (executed on UI thread after each file)
                        Action<string> postProcessAction = (processedPath) =>
                        {
                            // This action is called by FileOperationHelper after a file is successfully processed.
                            // We might not need to do anything specific here for the tab drop scenario,
                            // as ThumbnailListControl handles file system changes.
                            // However, you could add logging or specific UI updates if needed.
                            Debug.WriteLine($"[TabDrop] File processed: {processedPath}");
                        };

                        // Call FileOperationHelper to perform the copy/move
                        return await _fileOperationHelper.ExecuteFileOperation(
                            files.ToList(), // Convert array to List
                            targetFolderPath,
                            isCopy,
                            progress,
                            postProcessAction, // Pass the post-processing action
                            cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine("[TabDrop] File operation cancelled.");
                        return new List<string>(); // Return empty list on cancellation
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TabDrop] Error during file operation: {ex.Message}");
                        // Handle other exceptions during the background task if necessary
                        // Consider reporting the error back to the user via the UI thread
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"Error during file operation: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                        return new List<string>();
                    }
                });

                // Show notification after completion (if not cancelled)
                if (!cts.IsCancellationRequested && processedFiles.Any())
                {
                    string notificationMessage = isCopy ?
                        (string)Application.Current.FindResource("String_Thumbnail_FilesCopied") :
                        (string)Application.Current.FindResource("String_Thumbnail_FilesMoved");
                    // Consider showing a toast notification or updating status bar
                    StatusMessage = $"{processedFiles.Count} {notificationMessage}"; // Example status update
                                                                                     // ToastNotificationHelper.ShowRelativeTo(owner, notificationMessage); // If you have a toast helper
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions during dialog display or overall process
                Debug.WriteLine($"[TabDrop] Error processing dropped files: {ex.Message}");
                MessageBox.Show($"Error processing dropped files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Close dialog if it was shown and not cancelled
                if (closeDialog != null && cts != null && !cts.IsCancellationRequested)
                {
                    closeDialog();
                }
                cts?.Dispose();
            }
        }
    }
}
