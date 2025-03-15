# サムネイル更新処理の削除計画

## 1. ThumbnailListControl.xaml.cs からの削除対象

### 1.1 削除するメソッド
```csharp
// フィルタ関連
private async void OnFilterChanged(FilterChangedEventArgs args)
private void UpdateFilterButtonStates(int selectedRating)
private async Task ApplyFilterling(int rating)

// ソート関連
private async void OnSortOrderChanged(SortOrderChangedEventArgs args)
private async void SortToggle_Click(object sender, RoutedEventArgs e)
private async void SortTypeToggle_Click(object sender, RoutedEventArgs e)
private async Task SortThumbnailAsync(bool sortByDate, bool sortAscending, bool selectItem = false)

// サムネイル更新関連
private async Task LoadVisibleThumbnailsAsync(ScrollViewer scrollViewer)
private async Task ProcessThumbnailLoadQueue()
private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
private async Task OnWindowSizeChanged(ScrollViewer scrollViewer)
```

### 1.2 削除するフィールド
```csharp
private readonly Queue<Func<Task>> _thumbnailLoadQueue;
private readonly DispatcherTimer _thumbnailLoadTimer;
private bool _isFirstLoad;
private bool _pendingSelection;
private int _pendingSelectionIndex;
```

### 1.3 削除するイベントハンドラ登録
```csharp
// ThumbnailListControl コンストラクタから
_thumbnailLoadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
_thumbnailLoadTimer.Tick += async (s, e) => await ProcessThumbnailLoadQueue();
_thumbnailLoadTimer.Start();

// ThumbnailListControl_Loaded から
_eventAggregator.GetEvent<FilterChangedEvent>().Subscribe(OnFilterChanged);
_eventAggregator.GetEvent<SortOrderChangedEvent>().Subscribe(OnSortOrderChanged);
scrollViewer.ScrollChanged += OnScrollChanged;
window.SizeChanged += async (s, args) => await OnWindowSizeChanged(scrollViewer);
```

## 2. MainViewModel.cs からの削除対象

### 2.1 削除するメソッド
```csharp
// フィルタ関連
public async Task ApplyAllFilters(int ratingFilter, bool isPromptFilterEnabled, List<string> tagFilters, bool isTagFilterEnabled)
private void ApplyFilterAndUpdateSelection()
private bool FilterItems(object item)

// ソート関連
public async Task SortItemsAsync(bool sortByDate, bool sortAscending)
private int FindSortedInsertIndex(FileNodeModel newItem)
```

### 2.2 削除するフィールド
```csharp
private ICollectionView _filteredItems;
private bool _sortByDate;
private bool _sortAscending;
private int _currentRatingFilter;
private bool _isPromptFilterEnabled;
private readonly Dictionary<string, bool> _promptCache;
private readonly Dictionary<string, List<string>> _tagCache;
private List<string> _tagFilters;
private bool _isTagFilterEnabled;
```

## 3. 削除順序

1. イベントハンドラの登録解除
2. UI関連のメソッド削除
3. ビジネスロジックのメソッド削除
4. フィールドとプロパティの削除

## 4. 削除時の注意点

1. 依存関係の確認
   - 削除対象のメソッドを参照している箇所の特定
   - 削除による影響範囲の確認

2. コンパイルエラーの対応
   - イベント購読箇所の修正
   - 参照箇所の一時的な代替実装

3. デグレードの防止
   - 基本機能の動作確認
   - UI表示の確認

## 5. 一時的な代替実装

### 5.1 フィルタ機能
```csharp
// 一時的にすべての項目を表示
public async Task ApplyAllFilters(...)
{
    // 実装は空とし、すべての項目を表示
    return;
}
```

### 5.2 ソート機能
```csharp
// 一時的にファイル名順で固定
public async Task SortItemsAsync(...)
{
    var items = Items.OrderBy(x => x.FileName).ToList();
    Items.Clear();
    foreach (var item in items)
    {
        Items.Add(item);
    }
}
```

### 5.3 サムネイル更新
```csharp
// 一時的に即時読み込み
private async Task LoadVisibleThumbnailsAsync(...)
{
    // すべてのサムネイルを即時生成
    foreach (var item in Items)
    {
        await _thumbnailLoader.LoadThumbnailAsync(item);
    }
}
