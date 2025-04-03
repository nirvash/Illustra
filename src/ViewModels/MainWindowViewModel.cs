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

namespace Illustra.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private string _statusMessage = "";
        private bool _isLightTheme;
        private bool _isDarkTheme;
        private readonly IEventAggregator _eventAggregator;
        private readonly ThumbnailListViewModel _thumbnailListViewModel; // Renamed from _mainViewModel

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
                    // RaisePropertyChanged は SetProperty 内で呼ばれるため不要
                }
            }
        }

        public DelegateCommand OpenLanguageSettingsCommand { get; }
        public DelegateCommand OpenShortcutSettingsCommand { get; }
        public DelegateCommand OpenAdvancedSettingsCommand { get; }
        public DelegateCommand OpenImageGenerationWindowCommand { get; }
        public DelegateCommand<TabViewModel> CloseTabCommand { get; private set; }
        public DelegateCommand<TabViewModel> CloseOtherTabsCommand { get; private set; }
        public DelegateCommand<TabViewModel> DuplicateTabCommand { get; private set; }

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
            _eventAggregator.GetEvent<McpOpenFolderEvent>().Subscribe(OnMcpOpenFolderReceived, ThreadOption.UIThread);
            // OpenInNewTabEvent を購読 (ステップ9の接続)
            _eventAggregator.GetEvent<OpenInNewTabEvent>().Subscribe(args => HandleOpenInNewTab(args.FolderPath));


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

            // 現在のテーマを反映
            var settings = SettingsHelper.GetSettings();
            IsLightTheme = settings.Theme == "Light";
            IsDarkTheme = settings.Theme == "Dark";

            // ステータスメッセージの初期化
            StatusMessage = (string)Application.Current.Resources["String_Status_Ready"];
            // アプリケーション起動時に保存されたタブ状態を読み込む
            LoadTabStates();
            // 初期タブがない場合はデフォルトタブを追加
            // LoadTabStates でタブが復元されなかった場合のみデフォルトタブを追加
            if (Tabs.Count == 0)
            {
                var initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                // 設定から最後に開いたフォルダを取得するロジックを追加しても良い
                // var settings = SettingsHelper.GetSettings();
                // if (!string.IsNullOrEmpty(settings.LastFolderPath) && System.IO.Directory.Exists(settings.LastFolderPath))
                // {
                //     initialPath = settings.LastFolderPath;
                // }
                AddNewTab(initialPath);
            }
            // LoadTabStates でタブが復元された場合、最初のタブを選択して状態を適用
            else if (SelectedTab == null && Tabs.Count > 0)
            {
                SelectedTab = Tabs[0]; // 最初のタブを選択
                                       // SelectedTab のセッター内でイベントが発行される
            }
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
        }
        private bool CanExecuteCloseOtherTabs(TabViewModel tabToKeep) => tabToKeep != null && Tabs.Count > 1;

        private void ExecuteDuplicateTab(TabViewModel tabToDuplicate)
        {
            if (tabToDuplicate?.State == null) return;

            // TabState をディープコピー
            var newState = tabToDuplicate.State.Clone();
            var newTab = new TabViewModel(newState);

            // 元のタブの隣に挿入
            int originalIndex = Tabs.IndexOf(tabToDuplicate);
            if (originalIndex >= 0)
            {
                Tabs.Insert(originalIndex + 1, newTab);
            }
            else
            {
                Tabs.Add(newTab); // 見つからない場合は末尾に追加
            }

            SelectedTab = newTab; // 新しいタブを選択

            // コマンドの実行可能状態を更新
            CloseTabCommand.RaiseCanExecuteChanged();
            CloseOtherTabsCommand.RaiseCanExecuteChanged();
        }
        private bool CanExecuteDuplicateTab(TabViewModel tabToDuplicate) => tabToDuplicate != null;

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
        }

        /// <summary>
        /// アプリケーション終了時にタブ状態を保存する処理
        /// </summary>
        public void SaveTabStates()
        {
            var settings = SettingsHelper.GetSettings();
            // 現在のタブの状態をリストに変換して保存
            settings.TabStates = Tabs.Select(vm => vm.State).ToList();
            // TODO: 最後にアクティブだったタブのインデックスなども保存するとより良い
            SettingsHelper.SaveSettings(settings);
        }

        /// <summary>
        /// アプリケーション起動時にタブ状態を復元する処理
        /// </summary>
        private void LoadTabStates()
        {
            var settings = SettingsHelper.GetSettings();
            if (settings.TabStates != null && settings.TabStates.Any())
            {
                Tabs.Clear(); // 既存のタブ（もしあれば）をクリア
                foreach (var state in settings.TabStates)
                {
                    // 各状態から ViewModel を復元して追加
                    // 注意: FilterSettings や SortSettings が null の場合の考慮が必要かもしれない
                    var tabViewModel = new TabViewModel(state);
                    Tabs.Add(tabViewModel);
                }
                // TODO: 最後にアクティブだったタブを選択するロジックを追加
                // とりあえず最初のタブを選択
                if (Tabs.Count > 0)
                {
                    // 最初のタブを選択する (セッター内でイベントが発行される)
                    SelectedTab = Tabs[0];
                    // セッターでイベントが発行されるため、ここでの ApplyTabState 呼び出しは不要
                }
            }
        }
        /// <summary>
        /// フォルダツリーまたはお気に入りからフォルダが選択されたときの処理 (ステップ8)
        /// </summary>
        /// <param name="path">選択されたフォルダのパス</param>
        public void HandleFolderSelected(string path)
        {
            if (SelectedTab == null || SelectedTab.State == null) return; // アクティブなタブまたはその状態がない場合は何もしない
            if (string.IsNullOrEmpty(path)) return; // パスが無効な場合は何もしない

            // アクティブなタブの状態を更新
            SelectedTab.State.FolderPath = path;
            // SelectedTab.State.SelectedItemPath = null; // フォルダが変わったら選択は解除する（オプション）

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

            // 既存のタブで同じパスが開かれているか確認
            var existingTab = Tabs.FirstOrDefault(t => t.State?.FolderPath == path);

            if (existingTab != null)
            {
                // 既存のタブが見つかった場合は、それを選択状態にする
                SelectedTab = existingTab;
            }
            else
            {
                // 既存のタブがない場合は、新しいタブを追加して選択状態にする
                AddNewTab(path);
                // AddNewTab内でSelectedTabが設定され、そのセッターでイベントが発行されるため、
                // ここで明示的にサムネイルリストを更新する必要はないはず。
            }
        }
        /// <summary>
        /// McpOpenFolderEvent を受信したときの処理
        /// </summary>
        private void OnMcpOpenFolderReceived(McpOpenFolderEventArgs args)
        {
            HandleFolderSelected(args.FolderPath);
        }
    }
}
