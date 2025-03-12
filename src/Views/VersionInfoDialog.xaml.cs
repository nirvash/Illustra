using System.Windows;

namespace Illustra.Views
{
    public partial class VersionInfoDialog : Window
    {
        public VersionInfoDialog(string versionInfo)
        {
            InitializeComponent();
            VersionInfoText.Text = versionInfo;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(VersionInfoText.Text);
            MessageBox.Show(
                (string)FindResource("String_Common_CopyCompleted"),
                (string)FindResource("String_Common_Information"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
