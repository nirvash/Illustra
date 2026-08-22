using System.Windows;
using System.Windows.Controls;
using Illustra.ViewModels.Settings;

namespace Illustra.Views.Settings
{
    public partial class McpSettingsView : UserControl
    {
        private McpSettingsViewModel? ViewModel => DataContext as McpSettingsViewModel;

        public McpSettingsView()
        {
            InitializeComponent();
        }

        private void ShowHideToken_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.ShowToken = !ViewModel.ShowToken;
            }
        }
    }
}
