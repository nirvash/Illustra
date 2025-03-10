using System;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using Prism.Events;
using Prism.Ioc;
using Illustra.Models;
using Illustra.Events;
using Illustra.Helpers;
using System.Diagnostics;
using Illustra.Controls;
using Illustra.ViewModels;
using Illustra.Services;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace Illustra.Views
{
    public partial class PropertyPanelControl : UserControl
    {
        private IEventAggregator? _eventAggregator;
        private string _currentFilePath = string.Empty;
        private FileNodeModel? _currentFileNode;
        private readonly DatabaseManager _db = new();

        public static readonly DependencyProperty ImagePropertiesProperty =
            DependencyProperty.Register(
                nameof(ImageProperties),
                typeof(ImagePropertiesModel),
                typeof(PropertyPanelControl),
                new PropertyMetadata(null, OnImagePropertiesChanged));

        public ImagePropertiesModel? ImageProperties
        {
            get => (ImagePropertiesModel?)GetValue(ImagePropertiesProperty);
            set => SetValue(ImagePropertiesProperty, value);
        }

        private static void OnImagePropertiesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PropertyPanelControl control)
            {
                control.DataContext = e.NewValue;
                control.UpdatePropertiesDisplay();
            }
        }

        public PropertyPanelControl()
        {
            InitializeComponent();
            ImageProperties = new ImagePropertiesModel();
            DataContext = ImageProperties;

            Loaded += PropertyPanelControl_Loaded;
        }

        private void PropertyPanelControl_Loaded(object sender, RoutedEventArgs e)
        {
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<FileSelectedEvent>().Subscribe(OnFileSelected);
            _eventAggregator.GetEvent<RatingChangedEvent>().Subscribe(OnRatingChanged);
        }

        private void OnFileSelected(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath) && _currentFilePath != filePath)
            {
                _currentFilePath = filePath;
                _ = LoadFilePropertiesAsync(filePath); // ToDo: プロパティが表示されていない
                _ = LoadFileNodeAsync(filePath);
                Visibility = Visibility.Visible;
            }
            else if (string.IsNullOrEmpty(filePath))
            {
                _currentFilePath = string.Empty;
                ImageProperties = new ImagePropertiesModel();
                DataContext = ImageProperties;
                Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadFilePropertiesAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                ImageProperties = await ImagePropertiesModel.LoadFromFileAsync(filePath);
            }
            catch (Exception)
            {
                // Ignore
            }
        }

        private async Task LoadFileNodeAsync(string filePath)
        {
            try
            {
                _currentFileNode = await _db.GetFileNodeAsync(filePath);
                if (_currentFileNode != null)
                {
                    // FileNodeのRatingをImagePropertiesに反映
                    if (ImageProperties != null)
                    {
                        ImageProperties.Rating = _currentFileNode.Rating;
                    }
                    await UpdateRatingStars();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルノードの読み込み中にエラーが発生しました：{ex.Message}", "エラー",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task UpdateRatingStars(bool updateDb = false)
        {
            if (_currentFileNode != null && PropertiesGrid != null)
            {
                var buttonList = FindButtonsInVisualTree(PropertiesGrid);

                foreach (var button in buttonList)
                {
                    if (button.Tag != null && int.TryParse(button.Tag.ToString(), out int rating))
                    {
                        var starControl = FindVisualChild<RatingStarControl>(button);
                        if (starControl != null)
                        {
                            // レーティングに応じて塗りつぶし状態を設定
                            starControl.IsFilled = rating <= _currentFileNode.Rating;

                            // レーティング値に応じた色を設定（空白時は透明）
                            if (rating <= _currentFileNode.Rating)
                            {
                                starControl.StarFill = RatingHelper.GetRatingColor(rating);
                                starControl.TextColor = RatingHelper.GetTextColor(rating);
                            }
                            else
                            {
                                starControl.StarFill = Brushes.Transparent;
                                starControl.TextColor = Brushes.DarkGray;
                            }
                        }
                    }
                }

                if (updateDb)
                {
                    // DBに保存
                    await _db.UpdateRatingAsync(_currentFileNode.FullPath, _currentFileNode.Rating);
                }
            }
        }

        // Visual Tree内のすべてのButtonを探す再帰関数
        private List<Button> FindButtonsInVisualTree(DependencyObject parent)
        {
            var result = new List<Button>();

            // 子要素の数を取得
            int childCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // 子がButtonならリストに追加
                if (child is Button button)
                {
                    result.Add(button);
                }

                // 再帰的に子の子も検索
                result.AddRange(FindButtonsInVisualTree(child));
            }

            return result;
        }

        // ヘルパーメソッド: Visual Tree内の特定の型の子要素を検索
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            // Check if we're on the UI thread
            if (!parent.Dispatcher.CheckAccess())
            {
                // If not, invoke the method on the UI thread and wait for the result
                return parent.Dispatcher.Invoke(() => FindVisualChild<T>(parent));
            }

            // Now safely on UI thread
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T t)
                {
                    return t;
                }
                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private async void RatingStar_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFileNode == null || !(sender is Button button) ||
                !int.TryParse(button.Tag?.ToString(), out int rating))
                return;

            if (_currentFileNode.Rating == rating)
            {
                // 同じ星をクリックした場合はレーティングをクリア
                _currentFileNode.Rating = 0;
            }
            else
            {
                _currentFileNode.Rating = rating;
            }

            // ImagePropertiesのRatingも更新
            if (ImageProperties != null)
            {
                ImageProperties.Rating = _currentFileNode.Rating;
            }

            // レーティング変更イベントを発行
            _eventAggregator?.GetEvent<RatingChangedEvent>()?.Publish(
                new RatingChangedEventArgs { FilePath = _currentFilePath, Rating = _currentFileNode.Rating });

            // UIを更新
            await UpdateRatingStars(true);

            // 非同期操作を待機
            await Task.CompletedTask;
        }

        // 外部でレーティングが変更された場合の処理
        private async void OnRatingChanged(RatingChangedEventArgs args)
        {
            if (args.FilePath == _currentFilePath && _currentFileNode != null)
            {
                _currentFileNode.Rating = args.Rating;
                // ImagePropertiesのRatingも更新
                if (ImageProperties != null)
                {
                    ImageProperties.Rating = args.Rating;
                }
                await UpdateRatingStars();
            }
        }

        private void CopyOriginalText_Click(object sender, RoutedEventArgs e)
        {
            if (ImageProperties?.StableDiffusionResult != null)
            {
                try
                {
                    Clipboard.SetText(ImageProperties.UserComment);
                    MessageBox.Show("コメントをクリップボードにコピーしました。", "コピー完了",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"クリップボードへのコピーに失敗しました：{ex.Message}", "エラー",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void UpdatePropertiesDisplay()
        {
            if (ImageProperties != null)
            {
                // ファイルが実際に存在する場合のみレーティングを表示
                if (!string.IsNullOrEmpty(ImageProperties.FilePath) && File.Exists(ImageProperties.FilePath))
                {
                    _ = LoadFileNodeAsync(ImageProperties.FilePath);
                }
            }
        }

        private async void SendToWebUI_Click(object sender, RoutedEventArgs e)
        {
            if (ImageProperties?.StableDiffusionResult != null)
            {
                var prompt = ImageProperties.StableDiffusionResult.Tags;
                if (prompt != null)
                {
                    var payload = new
                    {
                        prompt = string.Join(",", prompt),
                    };

                    var jsonPayload = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    using (var client = new HttpClient())
                    {
                        var settingsViewModel = new SettingsViewModel(new LanguageService(_eventAggregator), _eventAggregator);
                        var url = settingsViewModel.WebUIUrl; // 設定画面で変更されたURLを使用
                        var response = await client.PostAsync(url, content);
                        if (response.IsSuccessStatusCode)
                        {
                            // MessageBox.Show("プロンプトが送信されました。", "送信成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show("プロンプトの送信に失敗しました。", "送信失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
        }
    }
}
