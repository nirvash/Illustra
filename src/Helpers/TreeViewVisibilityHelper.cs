using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Illustra.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;

    public static class TreeViewVisibilityHelper
    {
        /// <summary>
        /// TreeView内の表示されているアイテムを判定します（IsVisibleのみを使用）
        /// </summary>
        /// <param name="treeView">対象のTreeView</param>
        /// <returns>表示されているアイテムのリスト</returns>
        public static List<object> GetVisibleItems(TreeView treeView)
        {
            var visibleItems = new List<object>();

            if (treeView == null || treeView.Items.Count == 0)
                return visibleItems;

            // ルートレベルのアイテムから処理開始
            foreach (var item in treeView.Items)
            {
                var treeViewItem = treeView.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                CheckItemVisibility(treeViewItem, item, visibleItems);
            }

            return visibleItems;
        }

        /// <summary>
        /// アイテムが表示されているかを判定します
        /// </summary>
        public static bool IsItemVisible(TreeView treeView, object item)
        {
            if (treeView == null || item == null)
                return false;

            TreeViewItem treeViewItem = GetTreeViewItem(treeView, item);
            return treeViewItem != null && treeViewItem.IsVisible;
        }

        /// <summary>
        /// 指定されたTreeViewItemの可視性をチェックし、可視なら追加します
        /// </summary>
        private static void CheckItemVisibility(TreeViewItem treeViewItem, object dataItem, List<object> visibleItems)
        {
            if (treeViewItem == null)
                return;

            // シンプルにIsVisibleのみをチェック
            if (treeViewItem.IsVisible)
            {
                visibleItems.Add(dataItem);
            }

            // アイテムが展開されている場合は子要素も確認
            if (treeViewItem.IsExpanded)
            {
                foreach (var childItem in treeViewItem.Items)
                {
                    var childTreeViewItem = treeViewItem.ItemContainerGenerator.ContainerFromItem(childItem) as TreeViewItem;
                    CheckItemVisibility(childTreeViewItem, childItem, visibleItems);
                }
            }
        }

        /// <summary>
        /// 特定のデータアイテムに対応するTreeViewItemを取得します
        /// </summary>
        public static TreeViewItem GetTreeViewItem(TreeView treeView, object item)
        {
            if (treeView == null || item == null)
                return null;

            // ルートレベルで検索
            TreeViewItem treeViewItem = treeView.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            if (treeViewItem != null)
                return treeViewItem;

            // すべての展開済みアイテムを深さ優先で検索
            return FindTreeViewItemRecursively(treeView, item);
        }

        /// <summary>
        /// TreeView内を再帰的に検索してアイテムのTreeViewItemを取得します
        /// </summary>
        private static TreeViewItem FindTreeViewItemRecursively(ItemsControl container, object item)
        {
            // 展開されたすべてのノードを検索
            foreach (object obj in container.Items)
            {
                TreeViewItem childContainer = container.ItemContainerGenerator.ContainerFromItem(obj) as TreeViewItem;
                if (childContainer == null)
                    continue;

                // このアイテムが探しているものか確認
                if (obj == item)
                    return childContainer;

                // このノードが展開されていれば、子ノードも検索
                if (childContainer.IsExpanded)
                {
                    TreeViewItem result = FindTreeViewItemRecursively(childContainer, item);
                    if (result != null)
                        return result;
                }
            }

            return null;
        }
    }
}
