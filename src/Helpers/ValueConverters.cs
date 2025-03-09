using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Illustra.Helpers
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NumberToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int number)
            {
                return number > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue)
            {
                return string.IsNullOrEmpty(stringValue) ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// レーティング値を可視性に変換するコンバーター
    /// </summary>
    public class RatingToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int rating)
            {
                return rating > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// レーティング値を星記号（★☆）に変換するコンバーター
    /// </summary>
    public class RatingToStarsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int rating)
            {
                return RatingHelper.GetRatingStars(rating);
            }
            return "☆☆☆☆☆";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// レーティング値に対応した色に変換するコンバーター
    /// </summary>
    public class RatingToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int rating)
            {
                return RatingHelper.GetRatingColor(rating);
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// レーティング値を数字入り星マークに変換するコンバーター
    /// </summary>
    public class RatingToStarWithNumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int rating)
            {
                return RatingHelper.GetRatingStarWithNumber(rating);
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// レーティング値に対応したテキスト色に変換するコンバーター
    /// </summary>
    public class RatingToTextColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int rating)
            {
                return RatingHelper.GetTextColor(rating);
            }
            return RatingHelper.GetTextColor(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DateTimeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dateTime)
            {
                return dateTime != DateTime.MinValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// レーティング値を真偽値に変換するコンバーター
    /// </summary>
    public class RatingToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int rating)
            {
                return rating > 0;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class LeftPaddingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Thickness padding)
            {
                // 階層の深さに応じたインデント
                return padding.Left;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IndexToXMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3) return 50; // デフォルト値

            int index = values[0] is int i ? i : 0; // AlternationIndex
            int offset = values[1] != null ? System.Convert.ToInt32(values[1]) : 0; // 安全な変換
            double scale = values[2] != null ? System.Convert.ToDouble(values[2]) : 1.0; // 倍率も安全に変換

            return (index * scale) + offset; // 計算
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IndexToYMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3) return 50;

            int index = values[0] is int i ? i : 0;
            int offset = values[1] != null ? System.Convert.ToInt32(values[1]) : 0; // 安全な変換
            double scale = values[2] != null ? System.Convert.ToDouble(values[2]) : 1.0; // 倍率も安全に変換

            return (index * scale) + offset;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IndexToZIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                // 通常のケース：インデックスが大きいほど前面に表示
                return -index;

                // または、逆順に表示したい場合（最初のアイテムが最前面）
                // return -index; // マイナスをつけて反転

                // または、固定の最大値から引く場合（最初のアイテムが最前面）
                // int maxItems = 100; // 十分大きな値
                // return maxItems - index;
            }

            return 0; // デフォルト値
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
