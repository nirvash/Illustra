using System;
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

        public DelegateCommand NavigateToSettingsCommand { get; }
        public DelegateCommand OpenSettingsCommand { get; }

        public MainWindowViewModel()
        {
            // リソースディクショナリを再度更新
            ((App)Application.Current).UpdateResourceDictionaries();

            NavigateToSettingsCommand = new DelegateCommand(ExecuteNavigateToSettings);
            OpenSettingsCommand = new DelegateCommand(ExecuteOpenSettings);

            // ステータスメッセージの初期化
            StatusMessage = (string)Application.Current.Resources["String_Status_Ready"];
        }

        private void ExecuteNavigateToSettings()
        {
            // 設定画面を表示する
            ExecuteOpenSettings();
        }

        private void ExecuteOpenSettings()
        {
            // 設定画面をダイアログとして表示
            var settingsWindow = new SettingsWindow();
            settingsWindow.Owner = Application.Current.MainWindow;
            settingsWindow.ShowDialog();
            StatusMessage = (string)Application.Current.Resources["String_Status_Ready"];
        }
    }
}
