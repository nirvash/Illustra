using System.Windows;
using Illustra.ViewModels;
using MahApps.Metro.Controls;
using Prism.Ioc;

namespace Illustra.Views
{
    public partial class LanguageSettingsWindow : MetroWindow
    {
        private readonly LanguageSettingsViewModel _viewModel;

        public LanguageSettingsWindow()
        {
            InitializeComponent();

            // ViewModelをDIコンテナから取得
            _viewModel = ((App)Application.Current).Container.Resolve<LanguageSettingsViewModel>();
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
