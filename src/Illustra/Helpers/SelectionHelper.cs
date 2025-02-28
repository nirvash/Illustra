using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Illustra.Helpers
{
    /// <summary>
    /// ItemsControlで選択状態を実現するためのヘルパークラス
    /// </summary>
    public static class SelectionHelper
    {
        #region SelectedItem 添付プロパティ

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.RegisterAttached(
                "SelectedItem",
                typeof(object),
                typeof(SelectionHelper),
                new PropertyMetadata(null, OnSelectedItemChanged));

        public static object GetSelectedItem(DependencyObject obj)
        {
            return obj.GetValue(SelectedItemProperty);
        }

        public static void SetSelectedItem(DependencyObject obj, object value)
        {
            obj.SetValue(SelectedItemProperty, value);
        }

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ItemsControl itemsControl)
            {
                // 初めて設定される場合はイベントを登録
                if (e.OldValue == null && e.NewValue != null)
                {
                    itemsControl.Loaded += ItemsControl_Loaded;
                    itemsControl.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(ItemsControl_MouseLeftButtonUp), true);
                }
                else if (e.OldValue != null && e.NewValue == null)
                {
                    itemsControl.Loaded -= ItemsControl_Loaded;
                    itemsControl.RemoveHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(ItemsControl_MouseLeftButtonUp));
                }

                // 選択状態の更新を反映
                if (e.NewValue != null)
                {
                    UpdateSelection(itemsControl, e.NewValue);
                }
            }
        }

        private static void ItemsControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ItemsControl itemsControl)
            {
                object selectedItem = GetSelectedItem(itemsControl);
                if (selectedItem != null)
                {
                    UpdateSelection(itemsControl, selectedItem);
                }
            }
        }

        private static void ItemsControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ItemsControl itemsControl)
            {
                DependencyObject? originalSource = e.OriginalSource as DependencyObject;
                if (originalSource == null) return;

                // クリックされた要素を含むコンテナを見つける
                DependencyObject? container = FindAncestor<ContentPresenter>(originalSource);
                if (container != null)
                {
                    int index = itemsControl.ItemContainerGenerator.IndexFromContainer(container);
                    if (index >= 0 && index < itemsControl.Items.Count)
                    {
                        object selectedItem = itemsControl.Items[index];
                        SetSelectedItem(itemsControl, selectedItem);
                    }
                }
            }
        }

        private static void UpdateSelection(ItemsControl itemsControl, object selectedItem)
        {
            // 選択されたアイテムを取得
            int selectedIndex = -1;
            for (int i = 0; i < itemsControl.Items.Count; i++)
            {
                if (itemsControl.Items[i] == selectedItem)
                {
                    selectedIndex = i;
                    break;
                }
            }

            if (selectedIndex >= 0)
            {
                // アイテムコンテナを取得して選択状態を視覚的に更新
                var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(selectedIndex) as ContentPresenter;
                if (container != null)
                {
                    // データテンプレート内のBorderを検索して選択状態を視覚的に表現
                    Border? border = FindVisualChild<Border>(container);
                    if (border != null)
                    {
                        // 他のアイテムの選択状態をリセット
                        for (int i = 0; i < itemsControl.Items.Count; i++)
                        {
                            if (i != selectedIndex)
                            {
                                var otherContainer = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                                if (otherContainer != null)
                                {
                                    Border? otherBorder = FindVisualChild<Border>(otherContainer);
                                    if (otherBorder != null)
                                    {
                                        otherBorder.BorderBrush = Brushes.LightGray;
                                        otherBorder.BorderThickness = new Thickness(1);
                                    }
                                }
                            }
                        }

                        // 選択されたアイテムを強調表示
                        border.BorderBrush = Brushes.Blue;
                        border.BorderThickness = new Thickness(2);
                        border.BringIntoView();
                    }
                }
            }
        }

        #region ヘルパーメソッド

        // 指定された型の子要素を検索
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T result)
                    return result;

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        // 指定された型の祖先要素を検索
        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null && !(current is T))
            {
                current = VisualTreeHelper.GetParent(current);
            }
            return current as T;
        }

        #endregion

        #endregion
    }
}
