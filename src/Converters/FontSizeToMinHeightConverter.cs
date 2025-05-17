using System;
using System.Globalization;
using System.Windows.Data;

namespace Illustra.Converters
{
    /// <summary>
    /// フォントサイズからボタンのMinHeightを算出するコンバーター。
    /// 例: MinHeight = FontSize * 2.2 + 6 など、調整可能。
    /// </summary>
    public class FontSizeToMinHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double fontSize)
                return fontSize * 1.5 + 4;
            return 24.0; // デフォルトも小さめに
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
