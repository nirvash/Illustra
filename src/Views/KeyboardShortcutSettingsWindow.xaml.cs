using System.Windows;
using Illustra.ViewModels;
using MahApps.Metro.Controls;
using Prism.Ioc;

namespace Illustra.Views
{
    public partial class KeyboardShortcutSettingsWindow : MetroWindow
    {
        private readonly KeyboardShortcutSettingsViewModel _viewModel;

        public KeyboardShortcutSettingsWindow()
        {
            InitializeComponent();

            // ViewModelをDIコンテナから取得
            _viewModel = ((App)Application.Current).Container.Resolve<KeyboardShortcutSettingsViewModel>();
            DataContext = _viewModel;

            // CloseRequestedイベントを購読
            _viewModel.CloseRequested += (s, e) => Close();
        }
    }
}
