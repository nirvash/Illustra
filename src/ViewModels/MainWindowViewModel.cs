using System.Windows;
using Prism.Commands;
using Prism.Mvvm;
using Illustra.Views;

namespace Illustra.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private string _statusMessage = "";

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public DelegateCommand OpenLanguageSettingsCommand { get; }
        public DelegateCommand OpenShortcutSettingsCommand { get; }
        public DelegateCommand OpenAdvancedSettingsCommand { get; }

        public MainWindowViewModel()
        {
            OpenLanguageSettingsCommand = new DelegateCommand(ExecuteOpenLanguageSettings);
            OpenShortcutSettingsCommand = new DelegateCommand(ExecuteOpenShortcutSettings);
            OpenAdvancedSettingsCommand = new DelegateCommand(ExecuteOpenAdvancedSettings);

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
    }
}
