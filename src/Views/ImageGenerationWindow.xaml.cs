using Microsoft.Win32;
using System.Windows;
using System.ComponentModel;
using Illustra.Helpers;

namespace Illustra.Views
{
    public partial class ImageGenerationWindow : Window, INotifyPropertyChanged
    {
        private string _serverUrl = string.Empty;
        public string ServerUrl
        {
            get => _serverUrl;
            set
            {
                if (_serverUrl != value)
                {
                    _serverUrl = value;
                    OnPropertyChanged(nameof(ServerUrl));
                }
            }
        }

        private string _reforgePath = string.Empty;
        public string ReforgePath
        {
            get => _reforgePath;
            set
            {
                if (_reforgePath != value)
                {
                    _reforgePath = value;
                    OnPropertyChanged(nameof(ReforgePath));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ImageGenerationWindow()
        {
            InitializeComponent();
            DataContext = this;

            // 設定を読み込む
            var settings = SettingsHelper.GetSettings();
            ServerUrl = settings.ImageGenerationServerUrl;
            ReforgePath = settings.ImageGenerationReforgePath;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Reforge Folder"
            };

            if (dialog.ShowDialog() == true)
            {
                ReforgePath = dialog.FolderName;
                SaveSettings();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveSettings();
        }

        private void SaveSettings()
        {
            var settings = SettingsHelper.GetSettings();
            settings.ImageGenerationServerUrl = ServerUrl;
            settings.ImageGenerationReforgePath = ReforgePath;
            SettingsHelper.SaveSettings(settings);
        }
    }
}
