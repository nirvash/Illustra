using Prism.Commands;
using Prism.Mvvm;
using System.Windows;

namespace Illustra.ViewModels
{
    public class KeyboardShortcutSettingsViewModel : BindableBase
    {
        private string _shortcut1;
        private string _shortcut2;

        public string Shortcut1
        {
            get => _shortcut1;
            set => SetProperty(ref _shortcut1, value);
        }

        public string Shortcut2
        {
            get => _shortcut2;
            set => SetProperty(ref _shortcut2, value);
        }

        public DelegateCommand SaveCommand { get; private set; }
        public DelegateCommand CancelCommand { get; private set; }

        public KeyboardShortcutSettingsViewModel()
        {
            SaveCommand = new DelegateCommand(ExecuteSave);
            CancelCommand = new DelegateCommand(ExecuteCancel);
        }

        private void ExecuteSave()
        {
            // キーボードショートカットの設定を保存
            MessageBox.Show("キーボードショートカットの設定を保存しました。", "設定の保存", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExecuteCancel()
        {
            // 設定をキャンセル
        }
    }
}
