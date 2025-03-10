using System.Windows;
using Illustra.ViewModels;
using Prism.Ioc;

namespace Illustra.Views
{
    public partial class KeyboardShortcutSettingsWindow : Window
    {
        private readonly KeyboardShortcutSettingsViewModel _viewModel;

        public KeyboardShortcutSettingsWindow()
        {
            InitializeComponent();

            // ViewModelをDIコンテナから取得
            _viewModel = ((App)Application.Current).Container.Resolve<KeyboardShortcutSettingsViewModel>();
            DataContext = _viewModel;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
