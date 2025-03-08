using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Illustra.Controls
{
    // マウスオーバー用のAdornerクラス
    public class TreeViewItemHighlightAdorner : Adorner
    {
        private FrameworkElement _headerPart;

        public TreeViewItemHighlightAdorner(UIElement adornedElement)
            : base(adornedElement)
        {
            this.IsHitTestVisible = false;

            // PART_Headerを探す
            if (adornedElement is TreeViewItem treeViewItem)
            {
                _headerPart = treeViewItem.Template.FindName("PART_Header", treeViewItem) as FrameworkElement;
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            Rect rect = new Rect(new Point(0, 0), AdornedElement.RenderSize);
            if (_headerPart != null)
            {
                // ヘッダー部分の位置と大きさを取得
                GeneralTransform transform = _headerPart.TransformToAncestor(AdornedElement);
                rect = transform.TransformBounds(
                    new Rect(0, 0, _headerPart.ActualWidth, _headerPart.ActualHeight));
            }
            else
            {
                // ヘッダーが見つからない場合は全体に枠線
                rect = new Rect(new Point(0, 0), AdornedElement.RenderSize);
            }

            // 矩形を指定したピクセル数だけ広げる（ここでは各方向に2ピクセル）
            double padding = 1;
            Rect expandedRect = new Rect(
                rect.X - padding,
                rect.Y - padding,
                rect.Width + (padding * 2),
                rect.Height + (padding * 2)
            );

            drawingContext.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(40, 0, 120, 215)), // 半透明の青
                null, expandedRect);
        }
    }
}
