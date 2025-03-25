using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Illustra.Views.Settings
{
    public partial class ViewerSettingsView : UserControl
    {
        public ViewerSettingsView()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
