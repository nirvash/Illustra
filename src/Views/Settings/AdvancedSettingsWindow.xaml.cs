using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Illustra.ViewModels.Settings;
using MahApps.Metro.Controls;

namespace Illustra.Views.Settings
{
    public partial class AdvancedSettingsWindow : MetroWindow
    {
        private readonly AdvancedSettingsViewModel _viewModel;

        public AdvancedSettingsWindow()
        {
            InitializeComponent();
            _viewModel = new AdvancedSettingsViewModel();
            DataContext = _viewModel;

            // GeneralSettingsは初期表示のためLoadedイベントは不要
        }

        private void SettingsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item)
            {
                ShowSettingsView(item.Tag as string);
            }
        }

        private void ShowSettingsView(string tag)
        {
            if (GeneralSettings == null || ThumbnailSettings == null ||
                ViewerSettings == null || PropertyPanelSettings == null ||
                DeveloperSettings == null)
            {
                return;
            }

            GeneralSettings.Visibility = Visibility.Collapsed;
            ThumbnailSettings.Visibility = Visibility.Collapsed;
            ViewerSettings.Visibility = Visibility.Collapsed;
            PropertyPanelSettings.Visibility = Visibility.Collapsed;
            DeveloperSettings.Visibility = Visibility.Collapsed;

            switch (tag)
            {
                case "General":
                    GeneralSettings.Visibility = Visibility.Visible;
                    break;
                case "Thumbnail":
                    ThumbnailSettings.Visibility = Visibility.Visible;
                    break;
                case "Viewer":
                    ViewerSettings.Visibility = Visibility.Visible;
                    break;
                case "PropertyPanel":
                    PropertyPanelSettings.Visibility = Visibility.Visible;
                    break;
                case "Developer":
                    DeveloperSettings.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 設定値の検証
                if (!_viewModel.ValidateSettings())
                {
                    MessageBox.Show(
                        this,
                        (string)FindResource("String_Settings_ValidationError"),
                        (string)FindResource("String_Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // 設定の保存
                _viewModel.SaveSettings();

                DialogResult = true;
                Close();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"{FindResource("String_Settings_SaveError")}\n{ex.Message}",
                    (string)FindResource("String_Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
