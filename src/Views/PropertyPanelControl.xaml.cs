using System;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Prism.Events;
using Prism.Ioc;
using Illustra.Models;
using Illustra.Events;
using Illustra.Helpers;

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
                new PropertyMetadata(null));

        public ImagePropertiesModel? ImageProperties
        {
            get => (ImagePropertiesModel?)GetValue(ImagePropertiesProperty);
            set => SetValue(ImagePropertiesProperty, value);
        }

        public PropertyPanelControl()
        {
            InitializeComponent();
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
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                _currentFilePath = filePath;
                _ = LoadFilePropertiesAsync(filePath);
                _ = LoadFileNodeAsync(filePath);
            }
        }

        private async Task LoadFilePropertiesAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                ImageProperties = new ImagePropertiesModel
                {
                    FilePath = filePath,
                    FileName = fileInfo.Name,
                    FileSize = fileInfo.Length,
                    CreationTime = fileInfo.CreationTime,
                    LastModified = fileInfo.LastWriteTime,
                    FileType = fileInfo.Extension
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルのプロパティ読み込み中にエラーが発生しました：{ex.Message}", "エラー",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadFileNodeAsync(string filePath)
        {
            try
            {
                _currentFileNode = await _db.GetFileNodeAsync(filePath);
                if (_currentFileNode != null)
                {
                    UpdateRatingStars();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルノードの読み込み中にエラーが発生しました：{ex.Message}", "エラー",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateRatingStars()
        {
            if (_currentFileNode != null && PropertiesGrid != null)
            {
                foreach (var element in PropertiesGrid.Children)
                {
                    if (element is Button button && button.Tag != null &&
                        int.TryParse(button.Tag.ToString(), out int rating))
                    {
                        button.Content = rating <= _currentFileNode.Rating ? "★" : "☆";
                    }
                }
            }
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
                // 新しいレーティングを設定
                _currentFileNode.Rating = rating;
            }

            // レーティング変更イベントを発行
            _eventAggregator?.GetEvent<RatingChangedEvent>()?.Publish(
                new RatingChangedEventArgs { FilePath = _currentFilePath, Rating = _currentFileNode.Rating });

            // UIを更新
            UpdateRatingStars();
        }

        private void OnRatingChanged(RatingChangedEventArgs args)
        {
            if (args.FilePath == _currentFilePath && _currentFileNode != null)
            {
                _currentFileNode.Rating = args.Rating;
                UpdateRatingStars();
            }
        }
    }
}
