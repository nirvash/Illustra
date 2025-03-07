using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WpfToolkit.Controls;

namespace Illustra.Controls
{
    public class DraggableListView : ListView
    {
        private Point _startPoint;
        private bool _isDragging = false;
        private bool _isInternalSelectionChange = false;

        // ItemWidthプロパティ
        public static readonly DependencyProperty ItemWidthProperty =
            DependencyProperty.Register(
                "ItemWidth",
                typeof(double),
                typeof(DraggableListView),
                new PropertyMetadata(double.NaN));

        public double ItemWidth
        {
            get { return (double)GetValue(ItemWidthProperty); }
            set { SetValue(ItemWidthProperty, value); }
        }

        // ItemHeightプロパティ
        public static readonly DependencyProperty ItemHeightProperty =
            DependencyProperty.Register(
                "ItemHeight",
                typeof(double),
                typeof(DraggableListView),
                new PropertyMetadata(double.NaN));

        public double ItemHeight
        {
            get { return (double)GetValue(ItemHeightProperty); }
            set { SetValue(ItemHeightProperty, value); }
        }

        public DraggableListView()
        {
            // マウス関連のイベントハンドラのみ登録
            this.AddHandler(ListView.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(ListView_PreviewMouseLeftButtonDown), true);
            this.AddHandler(ListView.PreviewMouseMoveEvent,
                new MouseEventHandler(ListView_PreviewMouseMove), true);
            this.AddHandler(ListView.PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler(ListView_PreviewMouseLeftButtonUp), true);

            // キーボードイベントは削除（標準の動作を使用）

            // ItemContainerStyleの設定を追加
            this.ItemContainerStyle = CreateItemContainerStyle();

            // VirtualizingWrapPanelを使用するようにItemsPanelを設定
            var itemsPanelTemplate = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingWrapPanel)));
            itemsPanelTemplate.Seal();
            this.ItemsPanel = itemsPanelTemplate;
        }

        private Style CreateItemContainerStyle()
        {
            var style = new Style(typeof(ListViewItem));

            // IsSelectedバインディングの設定
            style.Setters.Add(new Setter(ListViewItem.IsSelectedProperty,
                new Binding("IsSelected") { Mode = BindingMode.TwoWay }));

            // ダブルクリックイベントの設定
            style.Setters.Add(new EventSetter(ListViewItem.MouseDoubleClickEvent,
                new MouseButtonEventHandler(OnItemDoubleClick)));

            return style;
        }

        private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item && item.DataContext != null)
            {
                // ダブルクリックイベントを発生させる
                RaiseEvent(new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, MouseButton.Left)
                {
                    RoutedEvent = MouseDoubleClickEvent,
                    Source = item.DataContext
                });
                e.Handled = true;
            }
        }

        private void ListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // ドラッグ開始位置を記録
            _startPoint = e.GetPosition(null);
            _isDragging = false;
            e.Handled = true;
        }

        private void ListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point position = e.GetPosition(null);

                // ドラッグ判定のための最小距離をチェック
                if (Math.Abs(position.X - _startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    StartDrag();
                }
            }
        }

        private void StartDrag()
        {
            _isDragging = true;

            // IsSelectedプロパティを取得
            var firstItem = this.Items.Cast<object>().FirstOrDefault();
            if (firstItem == null)
            {
                _isDragging = false;
                return;
            }

            var isSelectedProperty = firstItem.GetType().GetProperty("IsSelected");
            if (isSelectedProperty == null)
            {
                _isDragging = false;
                return;
            }

            // 選択されているアイテムの情報を取得
            var selectedItems = this.Items.Cast<object>()
                .Where(i => (bool)isSelectedProperty.GetValue(i))
                .ToList();

            if (selectedItems.Count == 0)
            {
                selectedItems = GetSelectedItemsFromUI();
            }

            if (selectedItems.Count > 0)
            {
                // ドラッグドロップ操作を開始
                DragDrop.DoDragDrop(this, selectedItems, DragDropEffects.Move);
            }
        }

        // UIから選択されたアイテムを取得する補助メソッド
        private List<object> GetSelectedItemsFromUI()
        {
            var selectedItems = new List<object>();

            for (int i = 0; i < this.Items.Count; i++)
            {
                var container = this.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (container != null && container.IsSelected)
                {
                    selectedItems.Add(this.Items[i]);
                }
            }

            return selectedItems;
        }

        private void ListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                // ドラッグではなかったのでイベント再発行
                MouseButtonEventArgs newEvent = new MouseButtonEventArgs(
                        e.MouseDevice, e.Timestamp, e.ChangedButton)
                {
                    RoutedEvent = UIElement.MouseDownEvent,  // MouseDown を発行
                    Source = sender
                };
                ((UIElement)sender).RaiseEvent(newEvent);
            }
            _isDragging = false;
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T)
                {
                    return (T)current;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }

    // アイテムデータクラス - INotifyPropertyChanged実装
    public class ListItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
