using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Illustra.ViewModels;

namespace Illustra.Controls
{
    public partial class PromptEditorControl : UserControl
    {
        private Point _dragStartPoint;
        private bool _isDragging;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public PromptEditorControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// ドラッグ&ドロップの開始判定
        /// </summary>
        private void TagListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point position = e.GetPosition(null);

                // ドラッグ開始判定
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var listBox = sender as ListBox;
                    var selectedItems = listBox?.SelectedItems;

                    if (selectedItems?.Count > 0)
                    {
                        _isDragging = true;
                        DragDrop.DoDragDrop(listBox, selectedItems, DragDropEffects.Move);
                        _isDragging = false;
                    }
                }
            }
        }

        /// <summary>
        /// ドロップ処理
        /// </summary>
        private void TagListBox_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(PromptTagViewModel)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void TagListBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(PromptTagViewModel)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void TagListBox_Drop(object sender, DragEventArgs e)
        {
            if (DataContext is PromptEditorViewModel viewModel)
            {
                var listBox = sender as ListBox;
                var dropPoint = e.GetPosition(listBox);
                var dropTarget = listBox?.GetItemAtPoint(dropPoint) as PromptTagViewModel;

                // ドラッグされているタグを取得
                var draggedTag = e.Data.GetData(typeof(PromptTagViewModel)) as PromptTagViewModel;
                if (draggedTag == null) return;

                // ドロップ位置のインデックスを取得
                int targetIndex = dropTarget != null
                    ? viewModel.Tags.IndexOf(dropTarget)
                    : viewModel.Tags.Count;

                // 現在のインデックスを取得
                int currentIndex = viewModel.Tags.IndexOf(draggedTag);
                if (currentIndex != -1 && currentIndex != targetIndex)
                {
                    // タグを移動
                    viewModel.Tags.RemoveAt(currentIndex);
                    if (targetIndex > currentIndex) targetIndex--;
                    viewModel.Tags.Insert(targetIndex, draggedTag);

                    // タグの順序を更新
                    for (int i = 0; i < viewModel.Tags.Count; i++)
                    {
                        viewModel.Tags[i].Order = i;
                    }
                }
            }
            e.Handled = true;
        }

        /// <summary>
        /// マウスボタンを押したときの処理
        /// </summary>
        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            _dragStartPoint = e.GetPosition(null);
        }
    }

    /// <summary>
    /// ListBoxの拡張メソッド
    /// </summary>
    public static class ListBoxExtensions
    {
        public static object GetItemAtPoint(this ListBox listBox, Point point)
        {
            var element = listBox.InputHitTest(point) as UIElement;
            while (element != null)
            {
                if (element is ListBoxItem)
                {
                    return (element as ListBoxItem).DataContext;
                }
                element = VisualTreeHelper.GetParent(element) as UIElement;
            }
            return null;
        }
    }
}
