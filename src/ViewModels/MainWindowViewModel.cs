using System.Windows;
using Prism.Commands;
using Prism.Mvvm;
using Illustra.Views;
using Illustra.Helpers;

namespace Illustra.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private string _statusMessage = "";
        private bool _isLightTheme;
        private bool _isDarkTheme;

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
        public DelegateCommand SetLightThemeCommand { get; }
        public DelegateCommand SetDarkThemeCommand { get; }

        public MainWindowViewModel()
        {
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
            StatusMessage = (string)Application.Current.Resources["String_Status_Ready"];
        }

        private void ExecuteOpenAdvancedSettings()
        {
            // 詳細設定画面をダイアログとして表示
            var advancedSettingsWindow = new AdvancedSettingsWindow
            {
                Owner = Application.Current.MainWindow
            };
            advancedSettingsWindow.ShowDialog();
            StatusMessage = (string)Application.Current.Resources["String_Status_Ready"];
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
    }
}
