using System.Windows;
using System.Windows.Input;
using Illustra.ViewModels;
using MahApps.Metro.Controls;

namespace Illustra.Views
{
    public partial class KeyboardShortcutSettingDialog : MetroWindow
    {
        private readonly KeyboardShortcutSettingDialogViewModel _viewModel;

        public KeyboardShortcutSettingDialog() : this(Key.None)
        {
        }

        public KeyboardShortcutSettingDialog(Key initialKey)
        {
            InitializeComponent();
            _viewModel = new KeyboardShortcutSettingDialogViewModel(initialKey);
            DataContext = _viewModel;

            // ViewModelのDialogResultの変更を監視
            _viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(KeyboardShortcutSettingDialogViewModel.DialogResult))
                {
                    DialogResult = _viewModel.DialogResult;
                }
            };
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            _viewModel.KeyDownCommand.Execute(e);
        }

        // 結果を取得するためのプロパティ
        public Key SelectedKey => _viewModel.SelectedKey;
        public bool IsCtrlPressed => _viewModel.IsCtrlPressed;
        public bool IsAltPressed => _viewModel.IsAltPressed;
        public bool IsShiftPressed => _viewModel.IsShiftPressed;
        public bool IsWindowsPressed => _viewModel.IsWindowsPressed;
        public string ShortcutText => _viewModel.ShortcutText;
    }
}
