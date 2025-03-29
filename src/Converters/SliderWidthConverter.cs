using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Illustra.Converters
{
    // Sliderの進捗部分の幅を計算するコンバータ
    public class SliderWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 3 &&
                values[0] is double value &&
                values[1] is double maximum &&
                values[2] is double actualWidth)
            {
                if (maximum == 0) return 0; // ゼロ除算防止
                double ratio = value / maximum;
                return actualWidth * ratio;
            }
            return 0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return new object[] { DependencyProperty.UnsetValue };
        }
    }
}
