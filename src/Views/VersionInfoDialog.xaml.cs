using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using System.Diagnostics;
using MahApps.Metro.Controls;

namespace Illustra.Views
{
    public partial class VersionInfoDialog : MetroWindow
    {
        private readonly string _versionString;

        public VersionInfoDialog()
        {
            InitializeComponent();

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            _versionString = $"{version.Major}.{version.Minor}.{version.Build}";
            var productVersion = "";
            try
            {
                string path = Environment.GetCommandLineArgs()[0];
                var ver = FileVersionInfo.GetVersionInfo(path);
                productVersion = ver.ProductVersion ?? _versionString;
            }
            catch
            {
                productVersion = _versionString;
            }

            VersionInfoText.Text = $"{(string)Application.Current.FindResource("String_About_Version")} \n{_versionString}\n\n{(string)Application.Current.FindResource("String_About_ProductVersion")} \n{productVersion}";
        }

        private static async Task<string> GetLatestReleaseTag()
        {
            const string url = "https://api.github.com/repos/nirvash/Illustra/releases/latest";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Illustra-VersionChecker");

                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    return doc.RootElement.GetProperty("tag_name").GetString();
                }
            }
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CheckUpdateButton.IsEnabled = false;
                UpdateStatusText.Text = "確認中...";

                var latestTag = await GetLatestReleaseTag();
                // タグから 'v' を削除してバージョン番号を取得
                var latestVersion = latestTag.TrimStart('v');

                // バージョン比較
                if (string.Compare(latestVersion, _versionString, StringComparison.OrdinalIgnoreCase) <= 0)
                {
                    UpdateStatusText.Text = (string)FindResource("String_About_IsLatestVersion");
                }
                else
                {
                    UpdateStatusText.Text = string.Format(
                        (string)FindResource("String_About_UpdateAvailable"),
                        latestTag); // オリジナルのタグを表示（vつきで）
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = (string)FindResource("String_About_CheckError");
                Debug.WriteLine($"Update check error: {ex.Message}");
            }
            finally
            {
                CheckUpdateButton.IsEnabled = true;
            }
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

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
