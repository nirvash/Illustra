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
            Debug.WriteLine($"PropertyPanelControl: OnFileSelected: {filePath}");
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
                ImageProperties = await ImagePropertiesModel.LoadFromFileAsync(filePath);
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
                // レーティングが設定されている場合は星を黄色にする
                // Visual Treeを再帰的に検索
                var buttonList = FindButtonsInVisualTree(PropertiesGrid);

                foreach (var button in buttonList)
                {
                    if (button.Tag != null && int.TryParse(button.Tag.ToString(), out int rating))
                    {
                        button.Content = rating <= _currentFileNode.Rating ? "★" : "☆";
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
                await UpdateRatingStars();
            }
        }
    }
}
