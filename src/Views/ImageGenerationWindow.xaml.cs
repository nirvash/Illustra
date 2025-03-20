using Microsoft.Win32;
using System.Windows;
using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Illustra.Helpers;
using Illustra.Models;
using MahApps.Metro.Controls;

namespace Illustra.Views
{
    public partial class ImageGenerationWindow : MetroWindow, INotifyPropertyChanged
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
                    _imageGenSettings.ServerUrl = value;
                    _imageGenSettings.Save();
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
                    _imageGenSettings.ReforgePath = value;
                    _imageGenSettings.Save();
                }
            }
        }

        private string _tags = string.Empty;
        public string Tags
        {
            get => _tags;
            set
            {
                if (_tags != value)
                {
                    _tags = value;
                    OnPropertyChanged(nameof(Tags));
                    _imageGenSettings.LastUsedTags = value;
                    _imageGenSettings.Save();
                }
            }
        }

        private readonly HttpClient _httpClient;
        private int _requestId = 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private ImageGenerationSettingsModel _imageGenSettings;

        public ImageGenerationWindow()
        {
            InitializeComponent();
            DataContext = this;

            _httpClient = new HttpClient();
            _imageGenSettings = ImageGenerationSettingsModel.Load();

            // 設定を読み込む
            ServerUrl = _imageGenSettings.ServerUrl;
            ReforgePath = _imageGenSettings.ReforgePath;
            Tags = _imageGenSettings.LastUsedTags;
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Tags))
            {
                MessageBox.Show(
                    "タグを入力してください。",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            try
            {
                var endpoint = $"{ServerUrl.TrimEnd('/')}/grimoire/set_prompt";
                var requestData = new
                {
                    prompt = Tags,
                    request_id = ++_requestId,
                    set_prompt = true,
                    action = "generate",
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(requestData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.PostAsync(endpoint, content);
                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show(
                        "プロンプトの送信に失敗しました。",
                        "エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"エラーが発生しました：{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
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
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _httpClient.Dispose();
        }
    }
}
