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
using Illustra.Models; // IllustraAppContext を使うために追加 (仮)

namespace Illustra.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private string _statusMessage = "";
        private bool _isLightTheme;
        private bool _isDarkTheme;
        private readonly IEventAggregator _eventAggregator;
        // private readonly IDialogCoordinator _dialogCoordinator; // 削除
        private readonly MainViewModel _mainViewModel;

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

        public DelegateCommand OpenLanguageSettingsCommand { get; }
        public DelegateCommand OpenShortcutSettingsCommand { get; }
        public DelegateCommand OpenAdvancedSettingsCommand { get; }
        public DelegateCommand OpenImageGenerationWindowCommand { get; }

        // MainViewModel からコマンドを公開
        public ICommand CopyCommand => _mainViewModel.CopyCommand;
        public ICommand PasteCommand => _mainViewModel.PasteCommand;
        public ICommand SelectAllCommand => _mainViewModel.SelectAllCommand;

        public DelegateCommand SetLightThemeCommand { get; }
        public DelegateCommand SetDarkThemeCommand { get; }

        public MainWindowViewModel()
        {
            // IllustraAppContext から MainViewModel を取得
            var appContext = ContainerLocator.Container.Resolve<IllustraAppContext>(); // using Illustra.Models; が必要
            _mainViewModel = appContext?.MainViewModel ?? throw new InvalidOperationException("MainViewModel could not be resolved from AppContext.");

            // IEventAggregator を解決してフィールドに設定
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>(); // using Prism.Events; が必要

            OpenLanguageSettingsCommand = new DelegateCommand(ExecuteOpenLanguageSettings);
            OpenShortcutSettingsCommand = new DelegateCommand(ExecuteOpenShortcutSettings);
            OpenAdvancedSettingsCommand = new DelegateCommand(ExecuteOpenAdvancedSettings);
            OpenImageGenerationWindowCommand = new DelegateCommand(ExecuteOpenImageGenerationWindow);
            SetLightThemeCommand = new DelegateCommand(ExecuteSetLightTheme);
            SetDarkThemeCommand = new DelegateCommand(ExecuteSetDarkTheme);

            // 現在のテーマを反映
            var settings = SettingsHelper.GetSettings();
            IsLightTheme = settings.Theme == "Light";
            IsDarkTheme = settings.Theme == "Dark";

            // ステータスメッセージの初期化
            StatusMessage = (string)Application.Current.Resources["String_Status_Ready"];
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

        // ShowProgressDialogAsync メソッドを削除
    }
}
