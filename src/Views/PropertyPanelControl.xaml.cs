using System.Windows;
using System.Windows.Controls;
using Illustra.Models;

namespace Illustra.Views
{
    public partial class PropertyPanelControl : UserControl
    {
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
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == ImagePropertiesProperty)
            {
                DataContext = ImageProperties;
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            DataContext = ImageProperties;
        }
    }
}
