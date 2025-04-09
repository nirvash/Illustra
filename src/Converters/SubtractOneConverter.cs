using System;
using System.Globalization;
using System.Windows.Data;

namespace Illustra.Converters
{
    public class SubtractOneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return Math.Max(0, intValue - 1); // 0未満にならないように
            }
            return 0; // デフォルト値
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException(); // 通常は不要
        }
    }
}
