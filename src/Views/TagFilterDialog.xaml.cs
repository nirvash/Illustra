using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Illustra.Views
{
    /// <summary>
    /// タグフィルタダイアログの相互作用ロジック
    /// </summary>
    public partial class TagFilterDialog : Window
    {
        public List<string> TagFilters { get; private set; } = new List<string>();
        private List<Grid> _tagInputGrids = new List<Grid>();

        // 複数タグを受け取るコンストラクタを追加
        public TagFilterDialog(List<string> currentTagFilters)
        {
            InitializeComponent();

            // 初期タグを設定
            if (currentTagFilters != null && currentTagFilters.Count > 0)
            {
                TagFilters = new List<string>(currentTagFilters);
                foreach (var tag in TagFilters)
                {
                    AddTagInputField(tag);
                }
            }

            // 少なくとも1つのタグ入力フィールドがあることを確認
            if (_tagInputGrids.Count == 0)
            {
                AddTagInputField();
            }

            // ダイアログが表示されたら最初のテキストボックスにフォーカスを設定
            Loaded += (s, e) =>
            {
                if (_tagInputGrids.Count > 0)
                {
                    var textBox = FindTextBoxInGrid(_tagInputGrids[0]);
                    textBox?.Focus();
                    textBox?.SelectAll();
                }
            };
        }

        private TextBox FindTextBoxInGrid(Grid grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is TextBox textBox)
                {
                    return textBox;
                }
            }
            return null;
        }

        private void AddTagInputField(string tagText = "")
        {
            // タグ入力用のグリッドを作成
            var grid = new Grid();
            grid.Margin = new Thickness(0, 5, 0, 5);

            // 列の定義
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // ラベル
            var label = new TextBlock
            {
                Text = (string)FindResource("String_TagFilter_EnterTag"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            // テキストボックス
            var textBox = new TextBox
            {
                Text = tagText,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(textBox, 1);
            grid.Children.Add(textBox);

            // 追加ボタン
            var addButton = new Button
            {
                Content = (string)FindResource("String_TagFilter_AddTag"),
                Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(5, 0, 5, 0),
                MinWidth = 60
            };
            addButton.Click += AddTagButton_Click;
            Grid.SetColumn(addButton, 2);
            grid.Children.Add(addButton);

            // 削除ボタン（最初の入力フィールド以外に表示）
            if (_tagInputGrids.Count > 0)
            {
                var removeButton = new Button
                {
                    Content = (string)FindResource("String_TagFilter_RemoveTag"),
                    Margin = new Thickness(0),
                    Padding = new Thickness(5, 0, 5, 0),
                    MinWidth = 60,
                    Tag = grid // 削除対象のグリッドを参照
                };
                removeButton.Click += RemoveTagButton_Click;
                Grid.SetColumn(removeButton, 3);
                grid.Children.Add(removeButton);
            }

            // グリッドをスタックパネルに追加
            TagsStackPanel.Children.Add(grid);
            _tagInputGrids.Add(grid);
        }

        private void AddTagButton_Click(object sender, RoutedEventArgs e)
        {
            AddTagInputField();
        }

        private void RemoveTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Grid gridToRemove)
            {
                TagsStackPanel.Children.Remove(gridToRemove);
                _tagInputGrids.Remove(gridToRemove);
            }
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            // すべてのタグ入力フィールドをクリア
            TagsStackPanel.Children.Clear();
            _tagInputGrids.Clear();

            // 空の入力フィールドを1つ追加
            AddTagInputField();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // すべてのテキストボックスから空でないタグを収集
            var newTags = new List<string>();
            foreach (var grid in _tagInputGrids)
            {
                var textBox = FindTextBoxInGrid(grid);
                if (textBox != null && !string.IsNullOrWhiteSpace(textBox.Text))
                {
                    string tag = textBox.Text.Trim();
                    if (!newTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    {
                        newTags.Add(tag);
                    }
                }
            }

            // 新しいタグリストを設定
            TagFilters = new List<string>(newTags);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
