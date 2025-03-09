# 画像キャッシュシステムの設計

## 概要
画像ビューアのキャッシュシステムを、LRUキャッシュからウィンドウベースのキャッシュに移行する設計です。

## インターフェース設計

### IImageCache
```csharp
public interface IImageCache
{
    // 画像の取得を試みる
    BitmapSource? GetImage(string path);

    // キャッシュに画像が存在するか確認（LRU順序に影響しない）
    bool HasImage(string path);

    // キャッシュの更新（ウィンドウの移動）
    void UpdateCache(List<FileNode> files, int currentIndex);

    // 初期化時のプリロード
    void PreloadCache(List<FileNode> files, int currentIndex);

    // キャッシュのクリア
    void Clear();

    // 現在のキャッシュ状態
    IReadOnlyDictionary<string, BitmapSource> CachedItems { get; }

    // キャッシュ状態変更通知
    event EventHandler<CacheStateChangedEventArgs> CacheStateChanged;
}

public class CacheStateChangedEventArgs : EventArgs
{
    public IReadOnlyDictionary<string, BitmapSource> CurrentCache { get; }
    public string[] AddedItems { get; }
    public string[] RemovedItems { get; }
}
```

### IImageLoader
```csharp
public interface IImageLoader
{
    // 画像の読み込み
    BitmapSource LoadImage(string path);

    // 画像のリソース解放
    void DisposeImage(BitmapSource image);
}
```

### WindowBasedImageCache
```csharp
public class WindowBasedImageCache : IImageCache
{
    private readonly int _forwardSize;  // 現在位置より後ろにキャッシュする数
    private readonly int _backwardSize; // 現在位置より前にキャッシュする数
    private readonly Dictionary<string, BitmapSource> _cache;
    private readonly IImageLoader _imageLoader;

    public WindowBasedImageCache(IImageLoader loader, int forwardSize = 3, int backwardSize = 3)
    {
        _imageLoader = loader;
        _forwardSize = forwardSize;
        _backwardSize = backwardSize;
        _cache = new Dictionary<string, BitmapSource>();
    }

    public void PreloadCache(List<FileNode> files, int currentIndex)
    {
        // 初期化時に前後の画像をプリロード
        var pathsToLoad = GetPathsInRange(files, currentIndex);
        foreach (var path in pathsToLoad)
        {
            if (!_cache.ContainsKey(path))
            {
                LoadImageAsync(path);
            }
        }
    }

    private HashSet<string> GetPathsInRange(List<FileNode> files, int currentIndex)
    {
        var paths = new HashSet<string>();
        int startIndex = Math.Max(0, currentIndex - _backwardSize);
        int endIndex = Math.Min(files.Count - 1, currentIndex + _forwardSize);

        for (int i = startIndex; i <= endIndex; i++)
        {
            paths.Add(files[i].FullPath);
        }

        return paths;
    }

    private async Task LoadImageAsync(string path)
    {
        try
        {
            var image = await Task.Run(() => _imageLoader.LoadImage(path));
            _cache[path] = image;
            NotifyCacheStateChanged(new[] { path }, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            // エラーログ記録
            System.Diagnostics.Debug.WriteLine($"画像の読み込みエラー: {ex.Message}");
        }
    }
}
```

## 利点

1. **予測可能なメモリ使用量**
   - キャッシュサイズが固定 (forwardSize + backwardSize + 1)
   - より効率的なメモリ管理

2. **分離された責務**
   - キャッシュロジックの分離
   - 画像読み込み処理の抽象化
   - テスト容易性の向上

3. **改善された初期化処理**
   - ビューワ起動時に自動的にプリロード
   - 非同期での画像読み込み
   - 読み込み状態の通知

## 実装の注意点

1. **スレッドセーフティ**
   - 画像の読み込み/解放は非同期で行う
   - キャッシュの更新時は適切な同期処理を行う
   - イベント通知は同期コンテキストを考慮

2. **リソース管理**
   - 不要になった画像は速やかに解放
   - メモリリークを防ぐため、Clear()時に全リソースを解放

3. **パフォーマンス最適化**
   - プリロードは非同期で実行
   - キャッシュの更新は差分のみを処理
   - 画像読み込みの優先順位付け

## 使用例

```csharp
public class ImageViewerWindow
{
    private readonly IImageCache _imageCache;

    public ImageViewerWindow(string filePath)
    {
        var imageLoader = new WpfImageLoader();
        _imageCache = new WindowBasedImageCache(imageLoader);

        // 初期化時にプリロード
        var fileNodes = Parent?.GetViewModel()?.FilteredItems.Cast<FileNode>().ToList();
        if (fileNodes != null)
        {
            int currentIndex = fileNodes.FindIndex(f => f.FullPath == filePath);
            _imageCache.PreloadCache(fileNodes, currentIndex);
        }

        // キャッシュ状態変更の購読
        _imageCache.CacheStateChanged += OnCacheStateChanged;
    }
}
