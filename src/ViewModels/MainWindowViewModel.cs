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
            // デフォルトのパスとしてマイピクチャを使用
            string defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            AddNewTab(defaultPath);
        }

        // --- タブ追加ヘルパーメソッド ---
        private void AddNewTab(string folderPath)
        {
            var newState = new TabState { FolderPath = folderPath };
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
            // 現在のタブの状態をリストに変換して保存
            settings.TabStates = Tabs.Select(vm => vm.State).ToList();
            // 最後にアクティブだったタブのインデックスを保存
            settings.LastActiveTabIndex = SelectedTab != null ? Tabs.IndexOf(SelectedTab) : -1;
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
        public void HandleFolderSelected(string path)
        {
            if (string.IsNullOrEmpty(path)) return; // パスが無効な場合は何もしない

            // タブが0個の場合は新しいタブを作成して開く
            if (Tabs.Count == 0)
            {
                AddNewTab(path);
                return; // 新しいタブを作成したので以降の処理は不要
            }

            // アクティブなタブのパスを更新する (元の動作)
            if (SelectedTab == null || SelectedTab.State == null) return; // アクティブなタブまたはその状態がない場合は何もしない

            // アクティブなタブの状態を更新
            SelectedTab.State.FolderPath = path;
            SelectedTab.State.SelectedItemPath = null; // フォルダが変わったら選択は解除する

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
            HandleFolderSelected(args.FolderPath);
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

    }
}
