using System.Windows;
using System.Windows.Media;

namespace Illustra.Helpers
{
    /// <summary>
    /// UI関連のユーティリティメソッドを提供するヘルパークラス
    /// </summary>
    public static class UIHelper
    {
        /// <summary>
        /// 指定された型の祖先要素を検索します
        /// </summary>
        /// <typeparam name="T">検索する要素の型</typeparam>
        /// <param name="current">検索を開始する要素</param>
        /// <returns>見つかった祖先要素、見つからない場合はnull</returns>
        public static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            // UIスレッドでの実行を確認
            if (!current.Dispatcher.CheckAccess())
            {
                return current.Dispatcher.Invoke(() => FindAncestor<T>(current));
            }

            do
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);

            return null;
        }

        /// <summary>
        /// 指定された型の子要素を検索します
        /// </summary>
        /// <typeparam name="T">検索する要素の型</typeparam>
        /// <param name="parent">検索を開始する親要素</param>
        /// <returns>見つかった子要素、見つからない場合はnull</returns>
        public static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            // UIスレッドでの実行を確認
            if (!parent.Dispatcher.CheckAccess())
            {
                return parent.Dispatcher.Invoke(() => FindVisualChild<T>(parent));
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T t)
                {
                    return t;
                }

                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        /// <summary>
        /// DataTemplate内の特定の名前を持つ要素を検索します
        /// </summary>
        /// <typeparam name="T">検索する要素の型</typeparam>
        /// <param name="container">検索を開始するコンテナ要素</param>
        /// <param name="elementName">検索する要素の名前（省略可）</param>
        /// <returns>見つかった要素、見つからない場合はnull</returns>
        public static T? FindElementInTemplate<T>(FrameworkElement container, string? elementName = null) where T : FrameworkElement
        {
            if (container == null)
                return null;

            // UIスレッドでの実行を確認
            if (!container.Dispatcher.CheckAccess())
            {
                return container.Dispatcher.Invoke(() => FindElementInTemplate<T>(container, elementName));
            }

            var childCount = VisualTreeHelper.GetChildrenCount(container);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(container, i) as DependencyObject;
                if (child == null) continue;

                // 目的の型と名前に一致する要素を検索
                if (child is T element && (string.IsNullOrEmpty(elementName) || element.Name == elementName))
                {
                    return element;
                }

                // 再帰的に子要素を検索
                if (child is FrameworkElement frameworkElement)
                {
                    var result = FindElementInTemplate<T>(frameworkElement, elementName);
                    if (result != null)
                        return result;
                }
            }

            return null;
        }
    }
}
