namespace Illustra.Helpers;

using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Illustra.Models;

/// <summary>
/// サムネイルの読み込みと管理を行うヘルパークラス
/// </summary>
public class ThumbnailLoaderHelper
{
    // 共通のダミー画像とエラー画像
    private static BitmapSource? _commonDummyImage;
    private static BitmapSource? _commonErrorImage;
    private static readonly object _staticLock = new object();

    private List<FileNodeModel> _imageFiles = new List<FileNodeModel>();
    private const int ThumbnailBatchSize = 100; // バッチ処理のサイズを増加
    private ConcurrentDictionary<string, BitmapSource> _thumbnailCache = new ConcurrentDictionary<string, BitmapSource>();
    private CancellationTokenSource? _cancellationTokenSource;
    private string _currentFolderPath = string.Empty;
    private readonly ItemsControl _thumbnailListBox;
    private int _thumbnailSize = 120;
    private ObservableCollection<FileNodeModel> _viewModelItems;

    /// <summary>
    /// サムネイルローダーを初期化します
    /// </summary>
    /// <param name="thumbnailListBox">サムネイルを表示するItemsControl</param>
    /// <param name="onThumbnailClick">サムネイルクリック時のアクション</param>
    /// <param name="viewModelItems">ViewModelのItemsコレクション</param>
    public ThumbnailLoaderHelper(ItemsControl thumbnailListBox, Action<string> onThumbnailClick, ObservableCollection<FileNodeModel> viewModelItems)
    {
        _thumbnailListBox = thumbnailListBox ?? throw new ArgumentNullException(nameof(thumbnailListBox));
        //            _onThumbnailClick = onThumbnailClick ?? throw new ArgumentNullException(nameof(onThumbnailClick));
        _viewModelItems = viewModelItems ?? throw new ArgumentNullException(nameof(viewModelItems));
    }

    /// <summary>
    /// 現在のフォルダパスを取得します
    /// </summary>
    public string CurrentFolderPath => _currentFolderPath;

    /// <summary>
    /// サムネイルのサイズを設定します
    /// </summary>
    public int ThumbnailSize
    {
        get => _thumbnailSize;
        set
        {
            if (_thumbnailSize != value)
            {
                _thumbnailSize = value;
                // サムネイルサイズが変わったので共通ダミー画像を初期化
                lock (_staticLock)
                {
                    _commonDummyImage = null;
                    _commonErrorImage = null;
                }
                // 現在のフォルダがあれば再ロード
                if (!string.IsNullOrEmpty(_currentFolderPath))
                {
                    LoadFileNodes(_currentFolderPath);
                }
            }
        }
    }

    /// <summary>
    /// サムネイルのロード完了イベント
    /// </summary>
    public event EventHandler? fileNodesLoaded;

    /// <summary>
    /// 指定されたフォルダのサムネイルを読み込みます
    /// </summary>
    /// <param name="folderPath">サムネイルを読み込むフォルダのパス</param>
    public async void LoadFileNodes(string folderPath)
    {
        // 前回のロード処理をキャンセル
        CancelAllLoading();

        // ViewModelのコレクションをクリア
        _viewModelItems.Clear();

        _currentFolderPath = folderPath;
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        var stopwatch = Stopwatch.StartNew(); // パフォーマンスログ用のストップウォッチを開始

        try
        {
            // まずファイル一覧だけを高速に取得
            var imageFilePaths = await Task.Run(() =>
                Directory.GetFiles(folderPath, "*.*")
                .Where(file => IsImageFile(file))
                .ToList(),
                token);

            if (token.IsCancellationRequested)
                return;

            // 共通のダミー画像を取得または生成
            var dummyImage = GetDummyImage();

            // 高速バッチ処理でViewModelにアイテムを追加
            await AddFileNodesToViewModelAsync(imageFilePaths, dummyImage, token);

            if (token.IsCancellationRequested)
                return;

            // サムネイルのロード完了イベントを発火
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                fileNodesLoaded?.Invoke(this, EventArgs.Empty);
            }, System.Windows.Threading.DispatcherPriority.Normal);

            // UIの更新を待つ
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (token.IsCancellationRequested)
                    return;

                var scrollViewer = FindVisualChild<ScrollViewer>(_thumbnailListBox);
                if (scrollViewer != null)
                {
                    int firstIndex = 0;
                    int lastIndex = Math.Min(ThumbnailBatchSize, _imageFiles.Count - 1);  // 初期表示として最初の一部をロード
                    await LoadMoreThumbnailsAsync(firstIndex, lastIndex);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // キャンセルされた場合は何もしない
            System.Diagnostics.Debug.WriteLine("サムネイル読み込み処理がキャンセルされました");
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) // キャンセルでない例外の場合だけメッセージを表示
            {
                MessageBox.Show($"サムネイルの読み込み中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// ファイルノードをすばやく作成してViewModelに追加
    /// </summary>
    private async Task AddFileNodesToViewModelAsync(List<string> filePaths, BitmapSource dummyImage, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew(); // パフォーマンスログ用のストップウォッチを開始

        // 大量のファイルパスからFileNodeModelのリストを効率的に作成
        _imageFiles = new List<FileNodeModel>(filePaths.Count);

        // UI更新を一度だけ行うため、全てのノードをまずメモリ上で生成
        foreach (var path in filePaths)
        {
            if (token.IsCancellationRequested)
                return;

            var node = new FileNodeModel
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                ThumbnailInfo = new ThumbnailInfo(dummyImage, ThumbnailState.NotLoaded) // 初期状態は未読み込み
            };
            _imageFiles.Add(node);
        }

        // UIスレッドで一括追加
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var uiStopwatch = Stopwatch.StartNew(); // UIスレッドの処理時間を計測

            // BulkObservableCollectionならAddRangeメソッドを使用できる
            if (_viewModelItems is BulkObservableCollection<FileNodeModel> bulkCollection)
            {
                // 一括で追加
                bulkCollection.AddRange(_imageFiles);
            }
            else
            {
                // 通常のObservableCollectionの場合は従来のバッチ処理を実行
                int batchSize = 500;
                for (int i = 0; i < _imageFiles.Count; i += batchSize)
                {
                    int currentBatchSize = Math.Min(batchSize, _imageFiles.Count - i);
                    var batch = _imageFiles.GetRange(i, currentBatchSize);

                    foreach (var fileNode in batch)
                    {
                        _viewModelItems.Add(fileNode);
                    }
                }
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// サムネイルを更新するメソッドを修正
    /// </summary>
    public async Task LoadMoreThumbnailsAsync(int startIndex, int endIndex)
    {
        // 範囲チェック
        startIndex = Math.Max(0, startIndex);
        endIndex = Math.Min(_imageFiles.Count - 1, endIndex);

        if (startIndex > endIndex || startIndex < 0 || endIndex >= _imageFiles.Count) return;

        for (int i = startIndex; i <= endIndex; i++)
        {
            var fileNode = _imageFiles[i];

            // サムネイルがダミー画像の場合またはまだロードされていない場合のみ処理
            if (fileNode != null &&
                (fileNode.ThumbnailInfo == null ||
                 (fileNode.ThumbnailInfo.Thumbnail == GetDummyImage() && fileNode.ThumbnailInfo.State != ThumbnailState.Error)))
            {
                // ロード中状態にする
                await _thumbnailListBox.Dispatcher.InvokeAsync(() =>
                {
                    fileNode.ThumbnailInfo = new ThumbnailInfo(GetDummyImage(), ThumbnailState.Loading);
                });

                // UI スレッドに戻って更新を行う
                await _thumbnailListBox.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        var thumbnailInfo = await GetOrCreateThumbnailAsync(fileNode.FullPath, GetCurrentCancellationToken());
                        fileNode.ThumbnailInfo = thumbnailInfo;
                    }
                    catch (Exception)
                    {
                        // 例外発生時はエラー状態に
                        fileNode.ThumbnailInfo = new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
                    }
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
        }
    }

    /// <summary>
    /// 与えられたキャンセルトークンを使って、現在のサムネイル読み込み処理をキャンセルします
    /// </summary>
    public CancellationToken GetCurrentCancellationToken()
    {
        return _cancellationTokenSource?.Token ?? CancellationToken.None;
    }

    /// <summary>
    /// 現在実行中のすべてのサムネイル読み込み処理をキャンセルします
    /// </summary>
    public void CancelAllLoading()
    {
        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private async Task<ThumbnailInfo> GetOrCreateThumbnailAsync(string? imagePath, CancellationToken cancellationToken)
    {
        // サムネイルサイズが変更された場合は、キャッシュキーにサイズ情報を含める
        string cacheKey = $"{imagePath}_{_thumbnailSize}";
        if (imagePath == null)
        {
            return new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
        }

        if (_thumbnailCache.TryGetValue(cacheKey, out var cachedThumbnail))
        {
            return new ThumbnailInfo(cachedThumbnail, cachedThumbnail == GetErrorImage() ? ThumbnailState.Error : ThumbnailState.Loaded);
        }

        try
        {
            var thumbnail = await ThumbnailHelper.CreateThumbnailAsync(imagePath, _thumbnailSize - 2, _thumbnailSize - 2);
            _thumbnailCache[cacheKey] = thumbnail;
            return new ThumbnailInfo(thumbnail, ThumbnailState.Loaded);
        }
        catch (Exception)
        {
            // エラーが発生した場合はエラー画像をキャッシュに追加
            var errorImage = GetErrorImage();
            _thumbnailCache[cacheKey] = errorImage;
            return new ThumbnailInfo(errorImage, ThumbnailState.Error);
        }
    }

    private bool IsImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension == ".jpg" || extension == ".jpeg" || extension == ".png" ||
               extension == ".gif" || extension == ".bmp" || extension == ".webp";
    }

    /// <summary>
    /// 共通のダミー画像を取得します。存在しない場合は生成します。
    /// </summary>
    private BitmapSource GetDummyImage()
    {
        lock (_staticLock)
        {
            if (_commonDummyImage == null)
            {
                _commonDummyImage = GenerateDummyImage(_thumbnailSize - 2, _thumbnailSize - 2);
            }
            return _commonDummyImage;
        }
    }

    /// <summary>
    /// 共通のエラー画像を取得します。存在しない場合は生成します。
    /// </summary>
    private BitmapSource GetErrorImage()
    {
        lock (_staticLock)
        {
            if (_commonErrorImage == null)
            {
                _commonErrorImage = GenerateErrorImage(_thumbnailSize - 2, _thumbnailSize - 2);
            }
            return _commonErrorImage;
        }
    }

    private BitmapSource GenerateDummyImage(int width, int height)
    {
        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            drawingContext.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, width, height));

            // シンプルな「読み込み中」の表示
            var pen = new Pen(Brushes.Silver, 1);
            drawingContext.DrawRectangle(null, pen, new Rect(5, 5, width - 10, height - 10));

            // 中央にクロスマーク
            drawingContext.DrawLine(new Pen(Brushes.Silver, 1), new Point(width / 2 - 10, height / 2), new Point(width / 2 + 10, height / 2));
            drawingContext.DrawLine(new Pen(Brushes.Silver, 1), new Point(width / 2, height / 2 - 10), new Point(width / 2, height / 2 + 10));
        }

        var renderTargetBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderTargetBitmap.Render(drawingVisual);
        renderTargetBitmap.Freeze(); // 重要：UIスレッド間で共有するためにFreezeする
        return renderTargetBitmap;
    }

    private BitmapSource GenerateErrorImage(int width, int height)
    {
        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            drawingContext.DrawRectangle(Brushes.MistyRose, null, new Rect(0, 0, width, height));

            // エラーアイコン (シンプルな X)
            var pen = new Pen(Brushes.Firebrick, 2);
            drawingContext.DrawLine(pen, new Point(width * 0.3, height * 0.3), new Point(width * 0.7, height * 0.7));
            drawingContext.DrawLine(pen, new Point(width * 0.7, height * 0.3), new Point(width * 0.3, height * 0.7));
        }

        var renderTargetBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderTargetBitmap.Render(drawingVisual);
        renderTargetBitmap.Freeze(); // 重要：UIスレッド間で共有するためにFreezeする
        return renderTargetBitmap;
    }

    // VisualTreeから特定の型の要素を検索するヘルパーメソッド
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T result)
                return result;

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }

        return null;
    }
}
