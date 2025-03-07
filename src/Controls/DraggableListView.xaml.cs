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

namespace Illustra.Controls
{
    public class DraggableListView : ListView
    {
        private Point _startPoint;
        private bool _isDragging = false;
        private bool _isInternalSelectionChange = false;

        public DraggableListView()
        {
            // マウス関連のイベントハンドラのみ登録
            this.AddHandler(ListView.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(ListView_PreviewMouseLeftButtonDown), true);
            this.AddHandler(ListView.MouseMoveEvent,
                new MouseEventHandler(ListView_MouseMove), true);
            this.AddHandler(ListView.PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler(ListView_PreviewMouseLeftButtonUp), true);

            // キーボードイベントは削除（標準の動作を使用）

            // ItemContainerStyleの設定を追加
            this.ItemContainerStyle = CreateItemContainerStyle();
        }

        private Style CreateItemContainerStyle()
        {
            var style = new Style(typeof(ListViewItem));

            // IsSelectedバインディングの設定
            style.Setters.Add(new Setter(ListViewItem.IsSelectedProperty,
                new Binding("IsSelected") { Mode = BindingMode.TwoWay }));

            return style;
        }

        private void ListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // ドラッグ開始位置を記録
            _startPoint = e.GetPosition(null);

            // クリックされた項目がListViewItemか確認する
            var element = e.OriginalSource as DependencyObject;
            var listViewItem = FindAncestor<ListViewItem>(element);

            if (listViewItem != null)
            {
                // クリックされたアイテムにフォーカスを設定
                listViewItem.Focus();

                var item = listViewItem.DataContext as ListItem;
                if (item == null) return;

                // Ctrlキーが押されている場合
                if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                {
                    // 選択状態を反転
                    _isInternalSelectionChange = true;
                    item.IsSelected = !item.IsSelected;
                    e.Handled = true;
                }
                // Shiftキーが押されている場合
                else if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                {
                    // 最後に選択したアイテムからこのアイテムまでを選択
                    _isInternalSelectionChange = true;

                    var items = this.Items.Cast<ListItem>().ToList();
                    // 現在選択されているアイテムを探す（最後の選択アイテムを見つける）
                    int lastSelectedIndex = -1;
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].IsSelected)
                        {
                            lastSelectedIndex = i;
                        }
                    }

                    // 現在クリックされたアイテムのインデックスを見つける
                    int currentIndex = items.IndexOf(item);

                    if (lastSelectedIndex != -1)
                    {
                        // 範囲を特定（開始と終了のインデックスを確認）
                        int startIndex = Math.Min(lastSelectedIndex, currentIndex);
                        int endIndex = Math.Max(lastSelectedIndex, currentIndex);

                        // 範囲内のすべてのアイテムを選択
                        for (int i = 0; i < items.Count; i++)
                        {
                            items[i].IsSelected = (i >= startIndex && i <= endIndex);
                        }
                    }
                    else
                    {
                        // 選択されているアイテムがない場合は、このアイテムだけを選択
                        item.IsSelected = true;
                    }

                    e.Handled = true;
                }
                // 修飾キーが押されていない場合
                else
                {
                    // 既に選択されている項目をクリックした場合
                    if (item.IsSelected)
                    {
                        // 複数選択されている場合
                        if (this.Items.Cast<ListItem>().Count(i => i.IsSelected) > 1)
                        {
                            // 他の選択を解除しないようにする（ドラッグのため）
                            _isInternalSelectionChange = true;
                            e.Handled = true;
                        }
                        // 単一選択の場合は選択解除を許可
                        else if (e.ClickCount == 1)
                        {
                            _isInternalSelectionChange = true;
                            item.IsSelected = false;
                            e.Handled = true;
                        }
                    }
                    // 選択されていない項目をクリックした場合
                    else
                    {
                        // 他の選択を全て解除して、この項目だけを選択
                        _isInternalSelectionChange = true;
                        foreach (var i in this.Items.Cast<ListItem>().Where(i => i.IsSelected))
                        {
                            i.IsSelected = false;
                        }
                        item.IsSelected = true;
                        e.Handled = true;
                    }
                }

                _isInternalSelectionChange = false;
            }
        }

        private void ListView_MouseMove(object sender, MouseEventArgs e)
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

            // 選択されているアイテムの情報を取得
            var selectedItems = this.Items.Cast<ListItem>()
                .Where(i => i.IsSelected)
                .ToList();

            if (selectedItems.Count > 0)
            {
                // ドラッグドロップ操作を開始
                DragDrop.DoDragDrop(this, selectedItems, DragDropEffects.Move);
            }

            _isDragging = false;
        }

        private void ListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
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
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
