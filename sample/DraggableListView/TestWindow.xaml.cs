using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using Illustra.Controls;
using System.Linq;

namespace DraggableListViewSample
{
    public partial class TestWindow : Window
    {
        public ObservableCollection<ListItem> Items { get; set; }

        public TestWindow()
        {
            InitializeComponent();

            Items = new ObservableCollection<ListItem>();
            for (int i = 1; i <= 20; i++)
            {
                Items.Add(new ListItem
                {
                    Id = i,
                    Name = $"アイテム {i}",
                    Description = $"これはアイテム {i} の説明です。"
                });
            }
            listView.ItemsSource = Items;

            // ドロップを許可
            listView.AllowDrop = true;
            // ドロップイベントを追加
            listView.Drop += ListView_Drop;

            DataContext = this;
        }

        private void ListView_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(List<ListItem>)) is List<ListItem> droppedItems)
            {
                // ドロップされたアイテムの名前を取得
                var itemNames = droppedItems.Select(item => item.Name).ToList();

                // アイテム名を表示
                MessageBox.Show(
                    $"以下のアイテムがドロップされました：\n{string.Join("\n", itemNames)}",
                    "ドロップされたアイテム",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // ステータステキストを更新
                statusText.Text = $"{droppedItems.Count}個のアイテムがドロップされました";
            }
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
    }
}
