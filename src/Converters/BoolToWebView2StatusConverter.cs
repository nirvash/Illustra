using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;

namespace Illustra.Converters
{
    public class BoolToWebView2StatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isInstalled)
            {
                string resourceKey = isInstalled ? "String_WebView2_Status_Installed" : "String_WebView2_Status_NotInstalled";
                var result = App.Current.FindResource(resourceKey) as string;
                Debug.WriteLine($"BoolToWebView2StatusConverter: {isInstalled} -> {result}");
                return result;
            }
            Debug.WriteLine($"BoolToWebView2StatusConverter: Invalid value type: {value?.GetType().Name ?? "null"}");
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
