using System.Windows;
using Illustra.ViewModels;
using Prism.Ioc;

namespace Illustra.Views
{
    public partial class LanguageSettingsWindow : Window
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
