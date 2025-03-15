using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows;

namespace Illustra.Helpers;

/// <summary>
/// VirtualizingStackPanelの拡張メソッド
/// </summary>
public static class VirtualizingPanelExtensions
{
    public static int GetFirstVisibleIndex(this VirtualizingStackPanel panel)
    {
        var scrollViewer = FindParentScrollViewer(panel);
        if (scrollViewer == null) return 0;

        var offset = scrollViewer.VerticalOffset;
        var itemHeight = GetItemHeight(panel);

        return itemHeight > 0 ? (int)(offset / itemHeight) : 0;
    }

    public static int GetLastVisibleIndex(this VirtualizingStackPanel panel)
    {
        var scrollViewer = FindParentScrollViewer(panel);
        if (scrollViewer == null) return 0;

        var offset = scrollViewer.VerticalOffset;
        var viewport = scrollViewer.ViewportHeight;
        var itemHeight = GetItemHeight(panel);

        return itemHeight > 0 ? (int)((offset + viewport) / itemHeight) : 0;
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject child)
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null && !(parent is ScrollViewer))
        {
            parent = VisualTreeHelper.GetParent(parent);
        }
        return parent as ScrollViewer;
    }

    private static double GetItemHeight(VirtualizingStackPanel panel)
    {
        var generator = panel.ItemContainerGenerator;
        if (((ItemContainerGenerator)generator).Status != GeneratorStatus.ContainersGenerated)
            return 0;

        // 最初の生成されたアイテムを探す
        var itemsControl = panel.TemplatedParent as ItemsControl;
        if (itemsControl == null) return 0;

        var itemCount = itemsControl.Items.Count;
        for (int i = 0; i < itemCount; i++)
        {
            var container = ((ItemContainerGenerator)generator).ContainerFromIndex(i) as FrameworkElement;
            if (container != null)
            {
                return container.ActualHeight;
            }
        }

        return 0;
    }
}
