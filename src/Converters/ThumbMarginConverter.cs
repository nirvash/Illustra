using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Illustra.Converters
{
    // Sliderのサム位置を計算するコンバータ
    public class ThumbMarginConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 4 &&
                values[0] is double value &&
                values[1] is double maximum &&
                values[2] is double actualWidth)
            {
                double thumbWidth = 20; // デフォルト値

                if (values[3] is double tagWidth && tagWidth > 0)
                {
                    thumbWidth = tagWidth;
                }

                if (maximum == 0) return new Thickness(0); // ゼロ除算防止
                double ratio = value / maximum;
                double position = (actualWidth - thumbWidth) * ratio;
                return new Thickness(position, 0, 0, 0);
            }
            return new Thickness(0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return new object[] { DependencyProperty.UnsetValue };
        }
    }
}
