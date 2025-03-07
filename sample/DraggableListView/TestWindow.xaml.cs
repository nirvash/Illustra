using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using Illustra.Controls;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DraggableListViewSample
{
    public partial class TestWindow : Window
    {
        public ObservableCollection<Node> Items { get; set; }

        public TestWindow()
        {
            InitializeComponent();

            Items = new ObservableCollection<Node>();
            for (int i = 1; i <= 20; i++)
            {
                Items.Add(new Node
                {
                    Id = i,
                    Name = $"アイテム {i}",
                    Description = $"これはアイテム {i} の説明です。",
                    IsSelected = false
                });
            }

            // ドロップを許可
            listView.AllowDrop = true;
            // ドロップイベントを追加
            listView.Drop += ListView_Drop;
            DataContext = Items;
        }

        private void ListView_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(List<Node>)) is List<Node> droppedItems)
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
                Items[1].Name = "fuga";
            }
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // ListViewItem内のアイテムがダブルクリックされた場合のみ処理
            if (e.OriginalSource is Node item)
            {
                MessageBox.Show($"アイテム '{item.Name}' がダブルクリックされました。",
                                "ダブルクリックイベント",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);

                statusText.Text = $"ダブルクリック: {item.Name}";
                Items[1].Name = "hoge";
            }
        }


        public class Node : INotifyPropertyChanged
        {
            private int _id;
            public int Id
            {
                get => _id;
                set
                {
                    _id = value;
                    OnPropertyChanged(nameof(Id));
                }
            }

            private string _name;
            public string Name
            {
                get => _name;
                set
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }

            private string _description;
            public string Description
            {
                get => _description;
                set
                {
                    _description = value;
                    OnPropertyChanged(nameof(Description));
                }
            }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected virtual void OnPropertyChanged(string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

        }

    }
}
