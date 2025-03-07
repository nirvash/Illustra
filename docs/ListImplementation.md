# WPF ListViewの複数選択・ドラッグ機能の設計ドキュメント

このドキュメントでは、WPF ListViewで以下の機能を実現するための設計とロジックについて説明します：

1. 複数選択機能
2. 選択されたアイテムのドラッグ（選択状態を維持）
3. 選択されたアイテムの非選択機能
4. ダブルクリックイベント処理

## 1. 全体のアーキテクチャ

### 1.1 データモデルによる選択状態の管理

標準的なWPF ListViewの選択メカニズムを使うと、ドラッグ操作などでListViewの選択状態が変わってしまう問題があります。これを解決するために、データモデル自体に選択状態を保持する設計を採用しています。

```csharp
public class ListItem : INotifyPropertyChanged
{
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

    // INotifyPropertyChangedの実装
    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

### 1.2 UI(ListViewItem)とデータモデルの選択状態の同期

ListViewItemの`IsSelected`プロパティとデータモデルの`IsSelected`プロパティを双方向バインディングすることで、UIとデータモデルの選択状態を同期します。

```xml
<ListView.ItemContainerStyle>
    <Style TargetType="{x:Type ListViewItem}">
        <Setter Property="IsSelected" Value="{Binding IsSelected, Mode=TwoWay}"/>
    </Style>
</ListView.ItemContainerStyle>
```

### 1.3 イベントハンドラの登録

すべてのマウスイベントは、コードビハインドで`AddHandler`メソッドを使用して登録します。これにより、より細かい制御が可能になります。

```csharp
listView.AddHandler(ListView.PreviewMouseLeftButtonDownEvent,
    new MouseButtonEventHandler(ListView_PreviewMouseLeftButtonDown), true);
listView.AddHandler(ListView.MouseMoveEvent,
    new MouseEventHandler(ListView_MouseMove), true);
listView.AddHandler(ListView.PreviewMouseLeftButtonUpEvent,
    new MouseButtonEventHandler(ListView_PreviewMouseLeftButtonUp), true);
```

## 2. 複数選択の実装

複数選択は、標準的なWindows UIのパターンに従って実装されています。

### 2.1 通常クリック

修飾キー（CtrlやShift）なしでクリックした場合：
- すでに選択されているアイテムがクリックされた場合、そのアイテムのみ選択解除
- 選択されていないアイテムがクリックされた場合、他のすべての選択を解除し、そのアイテムのみ選択

```csharp
// 既に選択されている項目をクリックした場合（単一選択のとき）
if (item.IsSelected && items.Count(i => i.IsSelected) == 1 && e.ClickCount == 1)
{
    item.IsSelected = false;
}
// 選択されていない項目をクリックした場合
else
{
    // 他の選択を全て解除して、この項目だけを選択
    foreach (var i in items.Where(i => i.IsSelected))
    {
        i.IsSelected = false;
    }
    item.IsSelected = true;
}
```

### 2.2 Ctrlキー + クリック

Ctrlキーを押しながらクリックした場合：
- クリックされたアイテムの選択状態を反転（選択→非選択、非選択→選択）
- 他の選択されたアイテムの選択状態は変更しない

```csharp
// Ctrlキーが押されている場合
if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
{
    // 選択状態を反転
    item.IsSelected = !item.IsSelected;
}
```

### 2.3 Shiftキー + クリック

Shiftキーを押しながらクリックした場合：
- 前回選択したアイテムから今回クリックしたアイテムまでの範囲を全て選択
- 範囲外のアイテムは選択解除

```csharp
// Shiftキーが押されている場合
else if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
{
    // 最後に選択したアイテムからこのアイテムまでを選択
    int lastSelectedIndex = -1;
    for (int i = 0; i < items.Count; i++)
    {
        if (items[i].IsSelected)
        {
            lastSelectedIndex = i;
        }
    }

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
}
```

## 3. 選択アイテムのドラッグ機能

### 3.1 ドラッグの検出

マウスが一定距離以上移動したときにドラッグ開始と判断：

```csharp
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
```

### 3.2 選択状態の維持

複数選択されたアイテムをドラッグするとき、標準のListViewではドラッグしたアイテムだけが選択され、他のアイテムの選択が解除されます。これを防ぐために：

1. クリックした時点でアイテムがすでに選択されていれば、標準の選択動作をキャンセル：
```csharp
// 既に選択されている項目をクリックした場合で、複数選択されている場合
if (item.IsSelected && items.Count(i => i.IsSelected) > 1)
{
    // 他の選択を解除しないようにする（ドラッグのため）
    e.Handled = true;
}
```

2. ドラッグ操作後も選択状態を維持するために、選択状態をデータモデルで管理：
```csharp
// 選択されているアイテムの情報を取得
List<ListItem> selectedItems = new List<ListItem>();
foreach (ListItem item in items.Where(i => i.IsSelected))
{
    selectedItems.Add(item);
}

// ドラッグドロップ操作を開始
DragDrop.DoDragDrop(listView, selectedItems, DragDropEffects.Move);
```

## 4. ダブルクリックイベント処理

ダブルクリックイベントは標準的なWPFのイベントハンドリングを使用：

```csharp
private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
{
    // ListViewItem内のアイテムがダブルクリックされた場合のみ処理
    if (e.OriginalSource is FrameworkElement element &&
        element.DataContext is ListItem item)
    {
        MessageBox.Show($"アイテム '{item.Name}' がダブルクリックされました。");
        statusText.Text = $"ダブルクリック: {item.Name}";
    }
}
```

## 5. 選択状態を視覚的に確認するための補助機能

ステータステキストでドラッグやダブルクリックの操作を表示：

```csharp
statusText.Text = $"{selectedItems.Count}個のアイテムがドラッグされました";
```

## 6. 実装上の注意点

### 6.1 イベント処理の順序

イベント処理の順序が重要です。特に:
- `PreviewMouseLeftButtonDown`：選択処理とドラッグ準備
- `MouseMove`：ドラッグの検出と開始
- `PreviewMouseLeftButtonUp`：ドラッグ終了

### 6.2 選択状態変更のフラグ

内部的な選択状態変更を追跡するために`isInternalSelectionChange`フラグを使用します。これにより、プログラムによる選択変更と、ユーザーによる選択変更を区別できます。

```csharp
private bool isInternalSelectionChange = false;
```

### 6.3 視覚ツリーの探索

クリックされた要素からListViewItemを見つけるために、VisualTreeHelperを使用：

```csharp
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
```

## 7. まとめ

この設計ではデータモデルが選択状態を持ち、UIとの双方向バインディングにより同期する方法を採用することで、WPF ListViewの標準動作の制限を回避しています。これにより、複数選択状態を維持したままドラッグする機能や、選択・非選択を柔軟に制御することが可能になります。
