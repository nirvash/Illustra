using System.Windows;
using System.Windows.Controls;
using System.IO;
using Illustra.Models;
using Illustra.Events;

namespace Illustra.Views
{
    public partial class PropertyPanelControl : UserControl
    {
        private IEventAggregator _eventAggregator;

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
            // Event Aggregatorを取得して、FileSelectedEventを購読する
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<FileSelectedEvent>().Subscribe(OnFileSelected);
        }

        private void OnFileSelected(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                // ファイルのプロパティを読み込んでImagePropertiesを更新
                LoadFilePropertiesAsync(filePath);
            }
        }

        private async void LoadFilePropertiesAsync(string filePath)
        {
            try
            {
                // 画像プロパティをロード（Exif情報などを含む詳細な情報）
                var properties = await ImagePropertiesModel.LoadFromFileAsync(filePath);

                // UIスレッドで更新
                await Dispatcher.InvokeAsync(() =>
                {
                    ImageProperties = properties;
                    DataContext = ImageProperties;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ファイルプロパティの取得中にエラーが発生しました: {ex.Message}");
                MessageBox.Show($"ファイルプロパティの取得中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
