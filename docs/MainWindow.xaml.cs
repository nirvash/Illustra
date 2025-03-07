using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace list
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<ListItem> items;
        private Point startPoint;
        private bool isDragging = false;
        private bool isInternalSelectionChange = false;
        
        public MainWindow()
        {
            InitializeComponent();
            
            // サンプルデータの初期化
            items = new ObservableCollection<ListItem>();
            for (int i = 1; i <= 20; i++)
            {
                items.Add(new ListItem
                {
                    Id = i,
                    Name = $"アイテム {i}",
                    Description = $"これはアイテム {i} の説明です。"
                });
            }
            
            listView.ItemsSource = items;
            listView.AddHandler(ListView.PreviewMouseLeftButtonDownEvent, 
                new MouseButtonEventHandler(ListView_PreviewMouseLeftButtonDown), true);
            listView.AddHandler(ListView.MouseMoveEvent, 
                new MouseEventHandler(ListView_MouseMove), true);
            listView.AddHandler(ListView.PreviewMouseLeftButtonUpEvent, 
                new MouseButtonEventHandler(ListView_PreviewMouseLeftButtonUp), true);
        }

        private void ListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // ドラッグ開始位置を記録
            startPoint = e.GetPosition(null);
            
            // クリックされた項目がListViewItemか確認する
            var element = e.OriginalSource as DependencyObject;
            var listViewItem = FindAncestor<ListViewItem>(element);
            
            if (listViewItem != null)
            {
                var item = listViewItem.DataContext as ListItem;
                if (item == null) return;
                
                // Ctrlキーが押されている場合
                if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                {
                    // 選択状態を反転
                    isInternalSelectionChange = true;
                    item.IsSelected = !item.IsSelected;
                    e.Handled = true;
                }
                // Shiftキーが押されている場合
                else if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                {
                    // 最後に選択したアイテムからこのアイテムまでを選択
                    isInternalSelectionChange = true;
                    
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
                        if (items.Count(i => i.IsSelected) > 1)
                        {
                            // 他の選択を解除しないようにする（ドラッグのため）
                            isInternalSelectionChange = true;
                            e.Handled = true;
                        }
                        // 単一選択の場合は選択解除を許可
                        else if (e.ClickCount == 1)
                        {
                            isInternalSelectionChange = true;
                            item.IsSelected = false;
                            e.Handled = true;
                        }
                    }
                    // 選択されていない項目をクリックした場合
                    else
                    {
                        // 他の選択を全て解除して、この項目だけを選択
                        isInternalSelectionChange = true;
                        foreach (var i in items.Where(i => i.IsSelected))
                        {
                            i.IsSelected = false;
                        }
                        item.IsSelected = true;
                        e.Handled = true;
                    }
                }
                
                isInternalSelectionChange = false;
            }
        }
        
        private void ListView_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !isDragging)
            {
                Point position = e.GetPosition(null);
                
                // ドラッグ判定のための最小距離をチェック
                if (Math.Abs(position.X - startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    StartDrag();
                }
            }
        }
        
        private void StartDrag()
        {
            isDragging = true;
            
            // 選択されているアイテムの情報を取得
            List<ListItem> selectedItems = new List<ListItem>();
            foreach (ListItem item in items.Where(i => i.IsSelected))
            {
                selectedItems.Add(item);
            }
            
            if (selectedItems.Count > 0)
            {
                // 選択されている項目を記憶
                var selectionSnapshot = items.Where(i => i.IsSelected).ToList();
                
                // ドラッグドロップ操作を開始
                DragDrop.DoDragDrop(listView, selectedItems, DragDropEffects.Move);
                
                statusText.Text = $"{selectedItems.Count}個のアイテムがドラッグされました";
            }
            
            isDragging = false;
        }
        
        private void ListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isDragging = false;
        }
        
        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // ListViewItem内のアイテムがダブルクリックされた場合のみ処理
            if (e.OriginalSource is FrameworkElement element && 
                element.DataContext is ListItem item)
            {
                MessageBox.Show($"アイテム '{item.Name}' がダブルクリックされました。", 
                                "ダブルクリックイベント", 
                                MessageBoxButton.OK, 
                                MessageBoxImage.Information);
                
                statusText.Text = $"ダブルクリック: {item.Name}";
            }
        }
        
        // 指定された型の先祖要素を検索するヘルパーメソッド
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
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}