using Illustra.Views;
using Illustra.ViewModels;

namespace Illustra.Helpers;

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Illustra.Models;
using Illustra.Helpers;
using Illustra.Extensions;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Threading;
using System.Collections.Concurrent;

/// <summary>
/// FileNodesLoadedEventArgsクラスを追加
/// </summary>
public class FileNodesLoadedEventArgs : EventArgs
{
    public string FolderPath { get; }

    public FileNodesLoadedEventArgs(string folderPath)
    {
        FolderPath = folderPath;
    }
}

/// <summary>
/// サムネイルの読み込みと管理を行うヘルパークラス
/// </summary>
public class ThumbnailLoaderHelper
{
    private static readonly SemaphoreSlim _folderLoadingSemaphore = new SemaphoreSlim(1, 1);
    private static readonly SemaphoreSlim _thumbnailLoadingSemaphore = new SemaphoreSlim(1, 1);
    private static BitmapSource? _commonDummyImage;
    private static BitmapSource? _commonErrorImage;
    private static readonly object _staticLock = new object();

    private string _currentFolderPath = string.Empty;
    private readonly ItemsControl _thumbnailListBox;
    private int _thumbnailSize = 120;
    private ObservableCollection<FileNodeModel> _viewModelItems;
    private AppSettingsModel _appSettings;
    private readonly ThumbnailListControl _control;
    private readonly MainViewModel _viewModel;
    private readonly Action<string> _selectCallback;
    private readonly DatabaseManager _db;
    private bool _isFileNodesLoadedEventFiring = false;
    private CancellationTokenSource? _folderLoadingCTS;
    private CancellationTokenSource? _thumbnailLoadCts;

    public event EventHandler<FileNodesLoadedEventArgs>? FileNodesLoaded;
    public event EventHandler<ScrollToItemRequestEventArgs>? ScrollToItemRequested;

    /// <summary>
    /// 現在読み込み中または読み込み済みのフォルダパスを取得します
    /// </summary>
    public string CurrentFolderPath => _currentFolderPath;

    private volatile bool _isFullscreenMode = false;

    public bool SortByDate { get; set; } = true;
    public bool SortAscending { get; set; } = true;

    /// <summary>
    /// 全画面表示モードを設定します
    /// </summary>
    public void SetFullscreenMode(bool isFullscreen)
    {
        _isFullscreenMode = isFullscreen;
        if (!isFullscreen)
        {
            // 全画面モード解除時は見えている範囲のサムネイルを再生成
            _ = LoadInitialThumbnailsAsync();
        }
    }

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

                // サイズのみを変更する場合は、サムネイルを再生成するだけでよい
                RefreshThumbnailSizes();
            }
        }
    }

    /// <summary>
    /// サムネイルローダーを初期化します
    /// </summary>
    public ThumbnailLoaderHelper(
        ItemsControl thumbnailListBox,
        Action<string> selectCallback,
        ThumbnailListControl control,
        MainViewModel viewModel,
        DatabaseManager db)
    {
        _thumbnailListBox = thumbnailListBox ?? throw new ArgumentNullException(nameof(thumbnailListBox));
        _selectCallback = selectCallback;
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _viewModelItems = viewModel.Items ?? throw new ArgumentNullException(nameof(viewModel.Items));

        // 設定を読み込む
        _appSettings = SettingsHelper.GetSettings();
        SortByDate = _appSettings.SortByDate;
        SortAscending = _appSettings.SortAscending;
    }

    /// <summary>
    /// 指定されたフォルダにアクセスできるかどうかを確認します
    /// </summary>
    /// <param name="folderPath">フォルダのパス</param>
    /// <returns>アクセス可能な場合はtrue、それ以外の場合はfalse</returns>
    private bool HasFolderAccess(string folderPath)
    {
        try
        {
            // フォルダ内のファイルを列挙してアクセス確認
            var files = Directory.EnumerateFiles(folderPath).Any();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 指定されたフォルダの画像のノードを読み込みます
    /// </summary>
    /// <param name="folderPath">画像のノードを読み込むフォルダのパス</param>
    /// <param name="initialSelectedFilePath">初期選択するファイルパス</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task LoadFileNodesAsync(string folderPath, string? initialSelectedFilePath = null, CancellationToken externalToken = default)
    {
        try
        {
            // キャンセルされていたら早期リターン
            if (externalToken.IsCancellationRequested)
                return;

            // 注意: ここでCancelAllLoadingを呼び出さない
            // CancelAllLoading();

            // フォルダ読み込み用の新しいトークンを作成
            var newCts = new CancellationTokenSource();
            var oldCts = Interlocked.Exchange(ref _folderLoadingCTS, newCts);
            if (oldCts != null)
            {
                try
                {
                    oldCts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // 既に破棄されている場合は無視
                }
            }

            // 外部トークンとフォルダ読み込みトークンを結合
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken, newCts.Token))
            {
                var combinedToken = linkedCts.Token;

                // 同時に複数のフォルダを読み込まないようにセマフォを使用
                await _folderLoadingSemaphore.WaitAsync(combinedToken);

                try
                {
                    LogHelper.LogWithTimestamp($"フォルダ読み込み開始: {folderPath}", LogHelper.Categories.ThumbnailLoader);
                    _currentFolderPath = folderPath;

                    // フォルダ内のファイル一覧を取得（すでにソート済み）
                    LogHelper.LogWithTimestamp("ファイル一覧の取得を開始", LogHelper.Categories.Performance);
                    var files = await Task.Run(() => GetImageFilesFromFolder(folderPath, combinedToken), combinedToken);
                    LogHelper.EndTimeMeasurement(LogHelper.StartTimeMeasurement("ファイル取得", LogHelper.Categories.Performance), "ファイル取得", $"{files.Count}件のファイルを取得");

                    combinedToken.ThrowIfCancellationRequested();

                    // ファイルノードの作成
                    LogHelper.LogWithTimestamp("ファイルノードの作成を開始", "ThumbnailLoader");
                    var fileNodes = await Task.Run(() => CreateFileNodes(files, combinedToken), combinedToken);

                    combinedToken.ThrowIfCancellationRequested();

                    // ファイルノードのソートは不要（すでにソート済み）
                    LogHelper.LogWithTimestamp("ファイルノードはすでにソート済みです", "ThumbnailLoader");

                    // ViewModelのItemsを更新
                    LogHelper.LogWithTimestamp($"ViewModelのItemsを更新開始: {fileNodes.Count}件", "ThumbnailLoader");

                    combinedToken.ThrowIfCancellationRequested();

                    // UIスレッドのDispatcherを使用（Application.Current.Dispatcherを使用）
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            // キャンセルされていたら何もしない
                            combinedToken.ThrowIfCancellationRequested();

                            // 新しいアイテムを追加
                            foreach (var node in fileNodes)
                            {
                                _viewModelItems.Add(node);
                            }

                            // 初期選択ファイルがある場合は選択
                            if (!string.IsNullOrEmpty(initialSelectedFilePath))
                            {
                                var itemToSelect = fileNodes.FirstOrDefault(n => n.FullPath.Equals(initialSelectedFilePath, StringComparison.OrdinalIgnoreCase));
                                if (itemToSelect != null)
                                {
                                    InvokeScrollToItemRequested(itemToSelect);
                                }
                            }

                            // ファイルノード読み込み完了イベントを発行
                            if (!_isFileNodesLoadedEventFiring)
                            {
                                _isFileNodesLoadedEventFiring = true;
                                try
                                {
                                    FileNodesLoaded?.Invoke(this, new FileNodesLoadedEventArgs(_currentFolderPath));
                                }
                                finally
                                {
                                    _isFileNodesLoadedEventFiring = false;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.LogError($"ViewModelのItems更新中にエラー: {ex.Message}", ex, "ThumbnailLoader");
                            throw;
                        }
                    }, DispatcherPriority.Normal);

                    LogHelper.LogWithTimestamp("完了", "ThumbnailLoader");
                }
                finally
                {
                    _folderLoadingSemaphore.Release();
                }
            }

            // 使用が終わったらCTSを破棄
            try
            {
                if (_folderLoadingCTS == newCts)
                {
                    _folderLoadingCTS = null;
                }
                newCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // 既に破棄されている場合は無視
            }
        }
        catch (OperationCanceledException)
        {
            LogHelper.LogWithTimestamp("キャンセルされました", "ThumbnailLoader");
        }
        catch (Exception ex)
        {
            LogHelper.LogError($"エラー: {ex.Message}", ex, "ThumbnailLoader");
        }
    }

    /// <summary>
    /// 初期表示時に見える範囲のサムネイルをロードします
    /// </summary>
    public async Task LoadInitialThumbnailsAsync(CancellationToken cancellationToken = default)
    {
        LogHelper.LogWithTimestamp("開始", "ThumbnailLoader");

        try
        {
            // サムネイル読み込み用の新しいトークンを作成
            var newCts = new CancellationTokenSource();
            var oldCts = Interlocked.Exchange(ref _thumbnailLoadCts, newCts);
            if (oldCts != null)
            {
                try
                {
                    oldCts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // 既に破棄されている場合は無視
                }
            }

            // 外部トークンとサムネイル読み込みトークンを結合
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, newCts.Token))
            {
                // フィルタされたアイテムの個数をチェック
                var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
                if (filteredItems.Count == 0)
                {
                    LogHelper.LogWithTimestamp("フィルタされたアイテムが存在しません", "ThumbnailLoader");
                    return;
                }

                var scrollViewer = FindVisualChild<ScrollViewer>(_thumbnailListBox);
                if (scrollViewer != null)
                {
                    // 最初の画面に表示される項目数を計算
                    var itemsPerRow = CalculateItemsPerRow(scrollViewer.ViewportWidth);
                    var visibleRows = (int)(scrollViewer.ViewportHeight / (_thumbnailSize + 4)); // マージンを考慮
                    var visibleItems = itemsPerRow * (visibleRows + 1); // 余裕を持って1行分多めにロード

                    var startIndex = 0;
                    var endIndex = Math.Min(visibleItems - 1, filteredItems.Count - 1);

                    LogHelper.LogWithTimestamp($"読み込み範囲: {startIndex}～{endIndex} (全{filteredItems.Count}件)", "ThumbnailLoader");

                    // 有効な範囲がある場合のみロード
                    if (endIndex >= startIndex && filteredItems.Count > 0)
                    {
                        await LoadMoreThumbnailsAsync(startIndex, endIndex, linkedCts.Token);
                    }
                    else
                    {
                        LogHelper.LogWithTimestamp($"読み込み範囲が無効です: {startIndex}～{endIndex}", "ThumbnailLoader");
                    }
                }
            }

            // 使用が終わったらCTSを破棄
            try
            {
                if (_thumbnailLoadCts == newCts)
                {
                    _thumbnailLoadCts = null;
                }
                newCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // 既に破棄されている場合は無視
            }
        }
        catch (OperationCanceledException)
        {
            LogHelper.LogWithTimestamp("処理がキャンセルされました", "ThumbnailLoader");
        }
        catch (Exception ex)
        {
            LogHelper.LogError($"エラー: {ex.Message}", ex, "ThumbnailLoader");
        }

        LogHelper.LogWithTimestamp("完了", "ThumbnailLoader");
    }

    /// <summary>
    /// サムネイルのサイズを更新し、すべてのサムネイルを再生成します
    /// </summary>
    public async void RefreshThumbnailSizes()
    {
        try
        {
            // サムネイル読み込み用の新しいキャンセルトークンを作成
            var newCts = new CancellationTokenSource();
            var oldCts = Interlocked.Exchange(ref _thumbnailLoadCts, newCts);
            if (oldCts != null)
            {
                try
                {
                    oldCts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // 既に破棄されている場合は無視
                }
            }

            var thumbnailLoadToken = newCts.Token;

            // ダミー画像を取得
            var dummyImage = GetDummyImage();

            // すべてのノードのサムネイル状態をリセット
            var fileNodes = _viewModelItems.Cast<FileNodeModel>().ToList();
            foreach (var node in fileNodes)
            {
                node.ThumbnailInfo = new ThumbnailInfo(dummyImage, ThumbnailState.NotLoaded);
            }

            var scrollViewer = FindVisualChild<ScrollViewer>(_thumbnailListBox);
            if (scrollViewer != null)
            {
                int firstIndex = 0;
                int lastIndex = Math.Min(20, fileNodes.Count - 1);

                await LoadMoreThumbnailsAsync(firstIndex, lastIndex, thumbnailLoadToken);
            }

            // 使用が終わったらCTSを破棄
            try
            {
                if (_thumbnailLoadCts == newCts)
                {
                    _thumbnailLoadCts = null;
                }
                newCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // 既に破棄されている場合は無視
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] サムネイルサイズ更新中にエラーが発生しました: {ex.Message}");
        }
    }

    private BitmapSource ResizeThumbnail(BitmapSource original, int width, int height)
    {
        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            drawingContext.DrawImage(original, new Rect(0, 0, width, height));
        }

        var resizedBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        resizedBitmap.Render(drawingVisual);
        resizedBitmap.Freeze();
        return resizedBitmap;
    }

    /// <summary>
    /// 表示されている範囲のサムネイルを読み込みます（キャンセル対応版）
    /// </summary>
    public async Task LoadMoreThumbnailsAsync(int startIndex, int endIndex, CancellationToken cancellationToken, bool highPriority = false, ScrollDirection scrollDirection = ScrollDirection.None)
    {
        try
        {
            LogHelper.LogWithTimestamp($"LoadMoreThumbnailsAsync メソッドが呼ばれました: ({startIndex} - {endIndex}, highPriority: {highPriority}, direction: {scrollDirection})", LogHelper.Categories.ThumbnailLoader);

            // 無効な範囲の場合は早期リターン
            if (endIndex < startIndex)
            {
                LogHelper.LogWithTimestamp($"無効な範囲が指定されました: {startIndex}～{endIndex}", LogHelper.Categories.ThumbnailLoader);
                return;
            }

            // インデックスの範囲を調整
            startIndex = Math.Max(0, startIndex);
            endIndex = Math.Min(_viewModelItems.Count - 1, endIndex);

            // 調整後も無効な範囲の場合は早期リターン
            if (endIndex < startIndex || startIndex < 0 || endIndex < 0)
            {
                LogHelper.LogWithTimestamp($"調整後の範囲が無効です: {startIndex}～{endIndex}", LogHelper.Categories.ThumbnailLoader);
                return;
            }

            LogHelper.LogWithTimestamp($"調整後の範囲: {startIndex}～{endIndex}", LogHelper.Categories.ThumbnailLoader);

            // 処理するインデックスのリストを作成
            var indicesToProcess = new List<int>();
            for (int i = startIndex; i <= endIndex; i++)
            {
                // 既に処理中または処理済みのアイテムはスキップ
                var item = _viewModelItems[i];
                if (item.ThumbnailInfo.State == ThumbnailState.Loading ||
                    item.ThumbnailInfo.State == ThumbnailState.Loaded)
                    continue;

                indicesToProcess.Add(i);
            }

            if (indicesToProcess.Count == 0)
            {
                LogHelper.LogWithTimestamp("処理対象のアイテムがありません（すべて処理済みまたは処理中）", LogHelper.Categories.ThumbnailLoader);
                return;
            }

            // スクロール方向に応じて処理順序を最適化
            switch (scrollDirection)
            {
                case ScrollDirection.Down:
                    // 下方向スクロール時は上から下へ処理
                    indicesToProcess = indicesToProcess.OrderBy(i => i).ToList();
                    LogHelper.LogWithTimestamp("下方向スクロール: 上から下へ処理", LogHelper.Categories.ThumbnailLoader);
                    break;

                case ScrollDirection.Up:
                    // 上方向スクロール時は下から上へ処理
                    indicesToProcess = indicesToProcess.OrderByDescending(i => i).ToList();
                    LogHelper.LogWithTimestamp("上方向スクロール: 下から上へ処理", LogHelper.Categories.ThumbnailLoader);
                    break;

                default:
                    // スクロール方向不明または初期表示時は中央から外側へ処理
                    int centerIndex = (startIndex + endIndex) / 2;
                    indicesToProcess = indicesToProcess
                        .OrderBy(i => Math.Abs(i - centerIndex))
                        .ToList();
                    LogHelper.LogWithTimestamp($"中央優先処理: 中央={centerIndex}, アイテム数={indicesToProcess.Count}", LogHelper.Categories.ThumbnailLoader);
                    break;
            }

            // 高優先度の場合は、最初の数個のアイテムを同期的に処理
            if (highPriority && indicesToProcess.Count > 0)
            {
                int syncProcessCount = Math.Min(4, indicesToProcess.Count);
                LogHelper.LogWithTimestamp($"高優先度処理: 同期処理数={syncProcessCount}", LogHelper.Categories.ThumbnailLoader);

                for (int i = 0; i < syncProcessCount; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    int index = indicesToProcess[i];
                    await CreateThumbnailAsync(index, cancellationToken);
                }

                // 同期処理したアイテムを除外
                indicesToProcess = indicesToProcess.Skip(syncProcessCount).ToList();
            }

            // 残りのアイテムを並列処理
            if (indicesToProcess.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                int maxParallel = highPriority ? 8 : 16;
                LogHelper.LogWithTimestamp($"{(highPriority ? "高優先度" : "通常優先度")}処理: 並列度={maxParallel}", LogHelper.Categories.ThumbnailLoader);

                await Task.WhenAll(
                    Partitioner.Create(indicesToProcess)
                        .GetPartitions(maxParallel)
                        .Select(async partition =>
                        {
                            using (partition)
                            {
                                while (partition.MoveNext() && !cancellationToken.IsCancellationRequested)
                                {
                                    await CreateThumbnailAsync(partition.Current, cancellationToken);
                                }
                            }
                        })
                );
            }
        }
        catch (OperationCanceledException)
        {
            LogHelper.LogWithTimestamp("サムネイル読み込み処理がキャンセルされました", LogHelper.Categories.ThumbnailLoader);
        }
        catch (Exception ex)
        {
            LogHelper.LogError("サムネイル読み込み中にエラーが発生しました", ex);
        }
    }

    /// <summary>
    /// サムネイル読み込みをキャンセルします
    /// </summary>
    public void CancelAllLoading()
    {
        CancelFolderLoading();
        CancelThumbnailLoading();
    }

    private void CancelThumbnailLoading()
    {
        var cts = Interlocked.Exchange(ref _thumbnailLoadCts, null);
        if (cts != null)
        {
            try
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                    Debug.WriteLine("[CANCEL] サムネイル読み込み処理をキャンセルしました");
                }
            }
            catch (ObjectDisposedException ex)
            {
                Debug.WriteLine($"[CANCEL] サムネイル読み込みキャンセル - トークンは既に破棄されています: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] サムネイル読み込みキャンセル処理中にエラーが発生しました: {ex.Message}");
            }
            finally
            {
                try
                {
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // 既に破棄されている場合は無視
                }
            }
        }
    }

    private void CancelFolderLoading()
    {
        var cts = Interlocked.Exchange(ref _folderLoadingCTS, null);
        if (cts != null)
        {
            try
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                    Debug.WriteLine($"[CANCEL] フォルダ読み込み処理をキャンセルしました: {_currentFolderPath}");
                }
            }
            catch (ObjectDisposedException ex)
            {
                Debug.WriteLine($"[CANCEL] フォルダ読み込みキャンセル - トークンは既に破棄されています: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] フォルダ読み込みキャンセル処理中にエラーが発生しました: {ex.Message}");
            }
            finally
            {
                try
                {
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // 既に破棄されている場合は無視
                }
            }
        }
    }

    private ThumbnailInfo GetOrCreateThumbnail(string? imagePath, CancellationToken cancellationToken)
    {
        if (imagePath == null)
        {
            return new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var thumbnail = ThumbnailHelper.CreateThumbnailOptimized(imagePath, _thumbnailSize - 2, _thumbnailSize - 2, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            return new ThumbnailInfo(thumbnail, ThumbnailState.Loaded);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"[CANCELLED] サムネイルの作成処理がキャンセルされました: {imagePath}");
            throw;
        }
        catch (Exception)
        {
            Debug.WriteLine($"[ERROR] サムネイルの作成中にエラーが発生しました: {imagePath}");
            var errorImage = GetErrorImage();
            return new ThumbnailInfo(errorImage, ThumbnailState.Error);
        }
    }


    public BitmapSource GetDummyImage()
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

    static private BitmapSource GenerateDummyImage(int width, int height)
    {
        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

            var pen = new Pen(Brushes.Silver, 1);
            drawingContext.DrawRectangle(null, pen, new Rect(5, 5, width - 10, height - 10));

            //            drawingContext.DrawLine(new Pen(Brushes.Silver, 1), new Point(width / 2 - 10, height / 2), new Point(width / 2 + 10, height / 2));
            //            drawingContext.DrawLine(new Pen(Brushes.Silver, 1), new Point(width / 2, height / 2 - 10), new Point(width / 2, height / 2 + 10));
        }

        var renderTargetBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderTargetBitmap.Render(drawingVisual);
        renderTargetBitmap.Freeze();
        return renderTargetBitmap;
    }

    private BitmapSource GenerateErrorImage(int width, int height)
    {
        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            drawingContext.DrawRectangle(Brushes.MistyRose, null, new Rect(0, 0, width, height));

            var pen = new Pen(Brushes.Firebrick, 2);
            drawingContext.DrawLine(pen, new Point(width * 0.3, height * 0.3), new Point(width * 0.7, height * 0.7));
            drawingContext.DrawLine(pen, new Point(width * 0.7, height * 0.3), new Point(width * 0.3, height * 0.7));
        }

        var renderTargetBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderTargetBitmap.Render(drawingVisual);
        renderTargetBitmap.Freeze();
        return renderTargetBitmap;
    }

    private int CalculateItemsPerRow(double viewportWidth)
    {
        // サムネイルサイズとマージンを考慮して1行あたりの表示可能数を計算
        return Math.Max(1, (int)(viewportWidth / (_thumbnailSize + 4)));
    }

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

    /// <summary>
    /// 新しいファイルノードを作成します
    /// </summary>
    /// <param name="path">ファイルパス</param>
    /// <returns>作成されたファイルノード</returns>
    public async Task<FileNodeModel?> CreateFileNodeAsync(string path)
    {
        try
        {
            // ファイルの存在確認はバックグラウンドで実行
            if (!await Task.Run(() => File.Exists(path)))
                return null;

            // DBからFileNodeModelを取得（バックグラウンドで実行）
            // 注意: DBアクセスは時間がかかる操作なのでバックグラウンドスレッドで実行
            var fileNode = await _db.CreateFileNodeAsync(path);

            if (fileNode == null)
            {
                // DBに存在しない場合は新規作成
                // ファイル情報の取得はバックグラウンドで実行
                var fileInfo = await Task.Run(() => new FileInfo(path));

                // 新しいFileNodeModelをバックグラウンドで作成
                fileNode = new FileNodeModel
                {
                    FileName = Path.GetFileName(path),
                    FullPath = path,
                    FolderPath = Path.GetDirectoryName(path) ?? string.Empty,
                    FileSize = fileInfo.Length,
                    CreationTime = fileInfo.CreationTime,
                    LastModified = fileInfo.LastWriteTime,
                    FileType = Path.GetExtension(path).ToLowerInvariant(),
                    IsImage = true // 画像ファイルのみを扱うため
                };

                // 注意: UIにバインドされるオブジェクトのプロパティ設定はUIスレッドで行う必要がある
                await _control.Dispatcher.InvokeAsync(() =>
                {
                    // ダミー画像を設定
                    fileNode.ThumbnailInfo = new ThumbnailInfo(GetDummyImage(), ThumbnailState.NotLoaded);
                    // 拡張メソッドを使用してプロパティを設定
                    fileNode.HasThumbnail = false;
                    fileNode.IsLoadingThumbnail = false;
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
            else
            {
                // DBから取得したモデルにダミー画像を設定
                // 注意: ThumbnailInfoはUIに表示される要素なのでUIスレッドで設定する
                await _control.Dispatcher.InvokeAsync(() =>
                {
                    if (fileNode.ThumbnailInfo == null)
                    {
                        fileNode.ThumbnailInfo = new ThumbnailInfo(GetDummyImage(), ThumbnailState.NotLoaded);
                    }
                    // 拡張メソッドを使用してプロパティを設定
                    fileNode.HasThumbnail = false;
                    fileNode.IsLoadingThumbnail = false;
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }

            return fileNode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ファイルノード作成エラー ({path}): {ex.Message}");
            return null;
        }
    }


    /// <summary>
    /// ファイルの名前変更処理を行います
    /// </summary>
    /// <param name="oldPath">変更前のファイルパス</param>
    /// <param name="newPath">変更後のファイルパス</param>
    /// <returns>作成された新しいファイルノード</returns>
    public async Task<FileNodeModel?> HandleFileRenamed(string oldPath, string newPath)
    {
        try
        {
            var fileNode = await _db.HandleFileRenamedAsync(oldPath, newPath);
            if (fileNode != null)
            {
                fileNode.ThumbnailInfo = new ThumbnailInfo(GetDummyImage(), ThumbnailState.NotLoaded);
            }
            return fileNode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ファイル名変更処理中にエラーが発生しました: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// フィルタリング後の選択位置を更新し、必要に応じてスクロールリクエストを発行します
    /// </summary>
    public void UpdateSelectionAfterFilter()
    {
        var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
        if (!filteredItems.Any()) return;

        Debug.Write("[フィルタリング後の選択位置更新] UpdateSelectionAfterFilter メソッドが呼ばれました");

        // 現在の選択アイテムがフィルタ結果に含まれているか確認
        var selectedItems = _viewModel.SelectedItems.Cast<FileNodeModel>().ToList();
        var firstVisibleSelectedItem = selectedItems.FirstOrDefault(item => filteredItems.Contains(item));

        if (firstVisibleSelectedItem != null)
        {
            // 選択アイテムが表示範囲内にある場合、そのアイテムまでスクロール
            ScrollToItemRequested?.Invoke(this, new ScrollToItemRequestEventArgs(firstVisibleSelectedItem));
        }
        else
        {
            // 選択アイテムが表示範囲内にない場合、最初のアイテムを選択してスクロール
            var firstItem = filteredItems.FirstOrDefault();
            if (firstItem != null)
            {
                _viewModel.SelectedItems.Clear();
                _viewModel.SelectedItems.Add(firstItem);
                ScrollToItemRequested?.Invoke(this, new ScrollToItemRequestEventArgs(firstItem));
            }
        }
    }

    /// <summary>
    /// スクロールイベントに応じたサムネイルのロード処理
    /// </summary>
    public async Task OnScrollChangedAsync(int startIndex, int endIndex)
    {
        Debug.WriteLine($"[スクロール変更] OnScrollChangedAsync メソッドが呼ばれました: ({startIndex} - {endIndex})");

        // 表示範囲のサムネイルを優先的にロード
        await LoadVisibleThumbnailsAsync(startIndex, endIndex);

        // スクロール停止を検知するための遅延
        await Task.Delay(200); // 200msの遅延でスクロール停止を検知

        // スクロールが停止したと判断したら先読みを開始
        await PreloadThumbnailsAsync(startIndex, endIndex);
    }

    /// <summary>
    /// 表示範囲のサムネイルをロード
    /// </summary>
    private async Task LoadVisibleThumbnailsAsync(int startIndex, int endIndex)
    {
        // 表示範囲のサムネイルをロードするロジック
        // ここに既存の表示範囲のサムネイルロード処理を実装
    }

    /// <summary>
    /// 先読みのサムネイルをロード
    /// </summary>
    private async Task PreloadThumbnailsAsync(int startIndex, int endIndex)
    {
        Debug.WriteLine($"[先読み] PreloadThumbnailsAsync メソッドが呼ばれました: ({startIndex} - {endIndex})");

        // 現在の表示範囲をログに出力
        Debug.WriteLine($"[表示範囲] 現在の表示範囲: {startIndex} - {endIndex}");


        // キャンセルトークンを取得
        var cancellationToken = _thumbnailLoadCts?.Token ?? CancellationToken.None;

        try
        {
            await _thumbnailLoadingSemaphore.WaitAsync(cancellationToken);
            try
            {
                var filteredNodes = _viewModel.FilteredItems.Cast<FileNodeModel>().ToArray();
                // 先読み範囲を設定
                int preloadStartIndex = Math.Max(0, endIndex + 1);
                int preloadEndIndex = Math.Min(filteredNodes.Length - 1, endIndex + 72); // 72個先まで先読み
                for (int i = preloadStartIndex; i <= preloadEndIndex; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Debug.WriteLine($"[CANCELLED] 先読み処理がキャンセルされました ({preloadStartIndex} - {preloadEndIndex})");
                        return;
                    }

                    var fileNode = filteredNodes[i];
                    if (fileNode != null &&
                        (fileNode.ThumbnailInfo == null ||
                         fileNode.ThumbnailInfo.State != ThumbnailState.Loaded))
                    {
                        try
                        {
                            var thumbnailInfo = await Task.Run(() => GetOrCreateThumbnail(fileNode.FullPath, cancellationToken), cancellationToken);
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                fileNode.ThumbnailInfo = thumbnailInfo;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            Debug.WriteLine($"[CANCELLED] 先読み処理がキャンセルされました: {fileNode.FullPath}");
                            continue;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ERROR] 先読み中にエラーが発生しました: {ex.Message}");
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                fileNode.ThumbnailInfo = new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
                            }
                        }
                    }
                }
            }
            finally
            {
                _thumbnailLoadingSemaphore.Release();
            }
        }
        finally
        {
            // 必要に応じてキャンセルトークンを破棄
        }
    }

    /// <summary>
    /// 指定された範囲のサムネイルを並列で読み込みます
    /// </summary>
    /// <param name="firstIndex">開始インデックス</param>
    /// <param name="lastIndex">終了インデックス</param>
    /// <param name="parallelism">並列度（同時に読み込むサムネイル数）</param>
    /// <returns>非同期タスク</returns>
    public async Task LoadThumbnailsInParallelAsync(int firstIndex, int lastIndex, int parallelism = 4)
    {
        // インデックスの範囲チェック
        if (firstIndex < 0 || lastIndex >= _viewModel.Items.Count || firstIndex > lastIndex)
            return;

        // 読み込み対象のインデックスリストを作成
        var indicesToLoad = new List<int>();
        for (int i = firstIndex; i <= lastIndex; i++)
        {
            indicesToLoad.Add(i);
        }

        // 中央から外側に向かって読み込むように並べ替え
        int centerIndex = (firstIndex + lastIndex) / 2;
        indicesToLoad = indicesToLoad
            .OrderBy(i => Math.Abs(i - centerIndex))
            .ToList();

        // 並列処理用のセマフォを作成
        using (var semaphore = new SemaphoreSlim(parallelism))
        {
            var tasks = new List<Task>();

            foreach (var index in indicesToLoad)
            {
                // セマフォを取得（並列数を制限）
                await semaphore.WaitAsync();

                // 各サムネイルの読み込みをタスクとして開始
                var task = Task.Run(async () =>
                {
                    try
                    {
                        // サムネイル読み込み処理
                        if (index < _viewModel.Items.Count)
                        {
                            var item = _viewModel.Items[index] as FileNodeModel;
                            if (item != null)
                            {
                                // 既存のサムネイル読み込みロジックを呼び出す
                                await LoadSingleThumbnailAsync(item);
                            }
                        }
                    }
                    finally
                    {
                        // 処理が完了したらセマフォを解放
                        semaphore.Release();
                    }
                });

                tasks.Add(task);
            }

            // すべてのタスクが完了するのを待機
            await Task.WhenAll(tasks);
        }
    }

    // 単一サムネイルを読み込むプライベートメソッド（既存のロジックを再利用）
    private async Task LoadSingleThumbnailAsync(FileNodeModel item)
    {
        // 既存のサムネイル読み込みロジックをここに実装
        // 現在のLoadMoreThumbnailsAsyncメソッドから、単一アイテムの読み込み部分を抽出
    }

    /// <summary>
    /// 指定されたFileNodeModelのサムネイルを非同期で作成します
    /// </summary>
    public async Task CreateThumbnailAsync(int index, CancellationToken cancellationToken)
    {
        if (index < 0 || index >= _viewModelItems.Count)
            return;

        var fileNode = _viewModelItems[index];
        if (fileNode == null)
            return;

        // 既に処理中または処理済みの場合はスキップ
        if (fileNode.ThumbnailInfo.IsLoadingThumbnail || fileNode.ThumbnailInfo.HasThumbnail)
            return;

        string fileName = System.IO.Path.GetFileName(fileNode.FullPath);
        LogHelper.LogWithTimestamp($"開始: [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);

        try
        {
            // 処理中フラグを設定
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                fileNode.ThumbnailInfo.IsLoadingThumbnail = true;
            }, DispatcherPriority.Send);

            // サムネイル生成処理
            BitmapSource thumbnail = null;
            try
            {
                // バックグラウンドスレッドでサムネイル生成
                LogHelper.LogWithTimestamp($"画像処理開始: [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);
                thumbnail = await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ThumbnailHelper.CreateThumbnailOptimized(fileNode.FullPath, ThumbnailSize, ThumbnailSize, cancellationToken);
                }, cancellationToken);

                LogHelper.LogWithTimestamp($"画像処理完了: [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);
            }
            catch (OperationCanceledException)
            {
                LogHelper.LogWithTimestamp($"キャンセル: [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);
                throw;
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"エラー: [{index}]{fileName}", ex);
                thumbnail = GetErrorImage();
            }

            // UIスレッドでサムネイル設定
            if (thumbnail != null)
            {
                // 重要: UIスレッドで同期的に実行し、確実に更新を反映させる
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // 既に読み込み済みの場合はスキップ（並列処理による競合を防ぐ）
                    if (fileNode.ThumbnailInfo.State == ThumbnailState.Loaded)
                    {
                        LogHelper.LogWithTimestamp($"スキップ（既に完了）: [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);
                        return;
                    }

                    // 重要: SetThumbnailメソッドを使用して、すべての関連プロパティを一度に更新
                    fileNode.SetThumbnail(thumbnail);

                    LogHelper.LogWithTimestamp($"完了: [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);
                }, DispatcherPriority.Send);
            }
            else
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // エラー状態を設定
                    fileNode.ThumbnailInfo = new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
                    fileNode.HasThumbnail = false;
                    LogHelper.LogWithTimestamp($"エラー画像設定: [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);
                }, DispatcherPriority.Send);
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセルされた場合は処理中フラグをリセット
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                fileNode.ThumbnailInfo.IsLoadingThumbnail = false;
            }, DispatcherPriority.Send);

            LogHelper.LogWithTimestamp($"キャンセル: [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);
        }
        catch (Exception ex)
        {
            // エラー発生時も処理中フラグをリセット
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                fileNode.ThumbnailInfo.IsLoadingThumbnail = false;
            }, DispatcherPriority.Send);

            LogHelper.LogError($"サムネイル生成エラー: [{index}]{fileName}", ex);
        }
    }

    private List<string> GetImageFilesFromFolder(string folderPath, CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(folderPath))
            {
                LogHelper.LogWithTimestamp($"フォルダが存在しません: {folderPath}", "ThumbnailLoader");
                return new List<string>();
            }

            // 処理時間計測用のストップウォッチ
            var sw = new Stopwatch();
            sw.Start();

            // サポートされている画像ファイル拡張子
            string[] supportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

            // フォルダ内のファイルを取得 (大文字小文字は区別されないのでこれでOK)
            var files = new List<string>();
            foreach (var extension in supportedExtensions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                files.AddRange(Directory.GetFiles(folderPath, $"*{extension}", SearchOption.TopDirectoryOnly));
            }

            // ファイル取得時間を計測
            var fileGetTime = sw.ElapsedMilliseconds;
            LogHelper.EndTimeMeasurement(sw, "ファイル取得", $"{files.Count}件のファイルを取得");

            // ソート処理の時間計測用にストップウォッチをリセット
            sw.Restart();

            // ファイル情報を取得してソート
            if (SortByDate)
            {
                LogHelper.LogWithTimestamp("ファイル一覧を日付順にソートします", "ThumbnailLoader");

                // ファイルパスと最終更新日時のペアを作成
                var fileInfos = files.Select(f => new
                {
                    Path = f,
                    LastWriteTime = File.GetLastWriteTime(f)
                }).ToList();

                // 日付でソート
                if (SortAscending)
                    fileInfos = fileInfos.OrderBy(f => f.LastWriteTime).ToList();
                else
                    fileInfos = fileInfos.OrderByDescending(f => f.LastWriteTime).ToList();

                // ソート済みのパスのみを返す
                files = fileInfos.Select(f => f.Path).ToList();
            }
            else
            {
                LogHelper.LogWithTimestamp("ファイル一覧を名前順にソートします", "ThumbnailLoader");

                // ファイル名でソート
                if (SortAscending)
                    files.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
                else
                    files.Sort((a, b) => string.Compare(Path.GetFileName(b), Path.GetFileName(a), StringComparison.OrdinalIgnoreCase));
            }

            // ソート処理時間を計測
            var sortTime = sw.ElapsedMilliseconds;
            LogHelper.EndTimeMeasurement(sw, "ソート処理");

            return files;
        }
        catch (UnauthorizedAccessException ex)
        {
            LogHelper.LogError($"アクセス権限エラー: {ex.Message}", ex);
            return new List<string>();
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            LogHelper.LogError($"エラー: {ex.Message}", ex);
            return new List<string>();
        }
    }

    private List<FileNodeModel> CreateFileNodes(List<string> files, CancellationToken cancellationToken)
    {
        var fileNodes = new List<FileNodeModel>(files.Count); // 容量を事前に確保
        LogHelper.LogWithTimestamp($"{files.Count}件のファイルからノードを作成します", "ThumbnailLoader");

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var fileInfo = new FileInfo(file);
                if (!fileInfo.Exists) continue;

                // FileNodeModelの基本情報を設定
                var fileNode = new FileNodeModel
                {
                    FullPath = fileInfo.FullName,
                    FolderPath = Path.GetDirectoryName(fileInfo.FullName),
                    FileName = fileInfo.Name,
                    CreationTime = fileInfo.CreationTime,
                    LastModified = fileInfo.LastWriteTime,
                    Rating = 0, // デフォルト値
                    ThumbnailInfo = new ThumbnailInfo(GetDummyImage(), ThumbnailState.NotLoaded)
                };

                fileNodes.Add(fileNode);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                LogHelper.LogError($"ファイルノード作成エラー ({file}): {ex.Message}", ex, "ThumbnailLoader");
            }
        }

        // ファイルノードを作成した後、バルクでデータベース情報を取得
        if (fileNodes.Count > 0)
        {
            try
            {
                string folderPath = Path.GetDirectoryName(files[0]);
                // 同期的に実行（非同期メソッドを同期的に呼び出す）
                EnrichFileNodesWithDatabaseInfoBulk(fileNodes, folderPath).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"DBからのバルク情報取得エラー: {ex.Message}", ex, "ThumbnailLoader");
            }
        }

        LogHelper.LogWithTimestamp($"{fileNodes.Count}件のノードを作成しました", "ThumbnailLoader");
        return fileNodes;
    }

    private void SortFileNodes(List<FileNodeModel> fileNodes)
    {
        LogHelper.LogWithTimestamp($"{fileNodes.Count}件のノードをソートします (日付順:{SortByDate}, 昇順:{SortAscending})", "ThumbnailLoader");

        // ソート前にファイル情報をキャッシュして、ファイルアクセスを減らす
        var sortCache = new Dictionary<string, DateTime>();

        if (SortByDate)
        {
            // 日付情報をキャッシュ
            foreach (var node in fileNodes)
            {
                if (!sortCache.ContainsKey(node.FullPath))
                {
                    try
                    {
                        sortCache[node.FullPath] = File.GetLastWriteTime(node.FullPath);
                    }
                    catch
                    {
                        // ファイルにアクセスできない場合は現在時刻を使用
                        sortCache[node.FullPath] = DateTime.Now;
                    }
                }
            }

            // キャッシュした日付でソート
            if (SortAscending)
                fileNodes.Sort((a, b) => sortCache[a.FullPath].CompareTo(sortCache[b.FullPath]));
            else
                fileNodes.Sort((a, b) => sortCache[b.FullPath].CompareTo(sortCache[a.FullPath]));
        }
        else
        {
            // ファイル名でソート（セカンダリソートキーなし）
            if (SortAscending)
                fileNodes.Sort((a, b) => string.Compare(
                    Path.GetFileName(a.FullPath),
                    Path.GetFileName(b.FullPath),
                    StringComparison.OrdinalIgnoreCase));
            else
                fileNodes.Sort((a, b) => string.Compare(
                    Path.GetFileName(b.FullPath),
                    Path.GetFileName(a.FullPath),
                    StringComparison.OrdinalIgnoreCase));
        }

        LogHelper.LogWithTimestamp("ソート完了", "ThumbnailLoader");
    }

    private async Task EnrichFileNodesWithDatabaseInfoBulk(List<FileNodeModel> fileNodes, string folderPath = null)
    {
        try
        {
            if (fileNodes.Count == 0) return;

            LogHelper.LogWithTimestamp($"{fileNodes.Count}件のファイルのメタデータをバルク取得します", "ThumbnailLoader");

            // フォルダパスが指定されていない場合は最初のファイルから取得
            if (string.IsNullOrEmpty(folderPath))
            {
                folderPath = Path.GetDirectoryName(fileNodes[0].FullPath);
            }

            LogHelper.LogWithTimestamp($"フォルダパス: {folderPath}", "ThumbnailLoader");

            // データベースからフォルダ内のすべてのファイルノードを取得
            var dbNodes = await _db.GetFileNodesAsync(folderPath);

            // ファイルパスをキーとしたディクショナリを作成（大文字小文字を区別しない）
            var dbNodeDict = dbNodes.ToDictionary(
                node => node.FullPath,
                node => node,
                StringComparer.OrdinalIgnoreCase);

            LogHelper.LogWithTimestamp($"{dbNodes.Count}件のノードをDBから取得しました", "ThumbnailLoader");

            // 各ファイルノードにデータベースの情報を設定
            int updatedCount = 0;
            foreach (var fileNode in fileNodes)
            {
                if (dbNodeDict.TryGetValue(fileNode.FullPath, out var dbNode))
                {
                    // データベースから取得した情報を設定
                    fileNode.Rating = dbNode.Rating;
                    // その他のプロパティがあれば設定
                    updatedCount++;
                }
            }

            LogHelper.LogWithTimestamp($"{updatedCount}件のノードにDBの情報を設定しました", "ThumbnailLoader");
        }
        catch (Exception ex)
        {
            LogHelper.LogError($"バルクメタデータ取得エラー: {ex.Message}", ex, "ThumbnailLoader");
        }
    }

    private async void EnrichFileNodeWithDatabaseInfo(FileNodeModel fileNode)
    {
        try
        {
            // データベースからファイルノードを取得
            var dbNode = await _db.GetFileNodeAsync(fileNode.FullPath);

            if (dbNode != null)
            {
                // データベースから取得した情報を設定
                fileNode.Rating = dbNode.Rating;
                // その他のプロパティがあれば設定
                // fileNode.Tags = dbNode.Tags;
            }
        }
        catch (Exception ex)
        {
            LogHelper.LogError($"DBからの情報取得エラー ({fileNode.FullPath}): {ex.Message}", ex, "ThumbnailLoader");
        }
    }

    // イベント発火時にUIスレッドで処理するように修正
    private void InvokeScrollToItemRequested(FileNodeModel item)
    {
        if (item == null)
            return;

        // UIスレッドで実行されていない場合は、UIスレッドにディスパッチ
        if (!System.Windows.Threading.Dispatcher.CurrentDispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                InvokeScrollToItemRequested(item);
            }, System.Windows.Threading.DispatcherPriority.Normal);
            return;
        }

        // UIスレッドで実行
        var args = new ScrollToItemRequestEventArgs(item);
        ScrollToItemRequested?.Invoke(this, args);
    }


}

/// <summary>
/// スクロールリクエストのイベント引数クラス
/// </summary>
public class ScrollToItemRequestEventArgs : EventArgs
{
    public FileNodeModel TargetItem { get; }

    public ScrollToItemRequestEventArgs(FileNodeModel targetItem)
    {
        TargetItem = targetItem;
    }
}

/// <summary>
/// スクロール方向を表す列挙型
/// </summary>
public enum ScrollDirection
{
    None,   // 方向不明または初期表示
    Up,     // 上方向へのスクロール
    Down    // 下方向へのスクロール
}


