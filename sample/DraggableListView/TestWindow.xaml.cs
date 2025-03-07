using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using GongSolutions.Wpf.DragDrop;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

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

            // ドラッグ＆ドロップの設定
            GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(listView, new CustomDropHandler(this));
            GongSolutions.Wpf.DragDrop.DragDrop.SetDragHandler(listView, new DefaultDragHandler());
            DataContext = Items;
        }

        public class CustomDropHandler : DefaultDropHandler
        {
            private readonly TestWindow _testWindow;

            public CustomDropHandler(TestWindow testWindow)
            {
                _testWindow = testWindow;
            }

            public override void Drop(IDropInfo dropInfo)
            {
                var items = new List<Node>();
                // 1つ
                if (dropInfo.Data is Node droppedItem)
                {
                    items.Add(droppedItem);
                }
                else if (dropInfo.Data != null)
                {
                    var objArray = dropInfo.Data as IEnumerable<object>;
                    var nodeArray = objArray.OfType<Node>().ToArray();
                    items.AddRange(nodeArray);
                }
                if (items.Count > 0)
                {
                    // ドロップされたアイテムの名前を取得
                    var itemNames = items.Select(item => item.Name).ToList();

                    // アイテム名を表示
                    MessageBox.Show(
                        $"以下のアイテムにドロップされました：\n{string.Join("\n", itemNames)}\n",
                        "ドロップされたアイテム: ???",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // ステータステキストを更新
                    _testWindow.statusText.Text = $"{items.Count}個のアイテムがドロップされました";
                }
            }
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // ListViewItem内のアイテムがダブルクリックされた場合のみ処理
            if (e.OriginalSource is FrameworkElement element && element.DataContext is Node item)
            {
                MessageBox.Show($"アイテム '{item.Name}' がダブルクリックされました。",
                                "ダブルクリックイベント",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);

                statusText.Text = $"ダブルクリック: {item.Name}";
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
