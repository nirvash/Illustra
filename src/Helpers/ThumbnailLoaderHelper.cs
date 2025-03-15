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
using System.ComponentModel;
using System.Windows.Controls.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WpfToolkit.Controls;

/// <summary>
/// サムネイルの読み込みと管理を行うヘルパークラス
/// </summary>
public class ThumbnailLoaderHelper
{
    private static BitmapSource? _commonDummyImage;
    private static BitmapSource? _commonErrorImage;
    private static readonly object _staticLock = new object();

    private string _currentFolderPath = string.Empty;
    private ItemsControl? _thumbnailListBox;
    private int _thumbnailSize = 120;
    private ObservableCollection<FileNodeModel> _viewModelItems;
    private readonly AppSettings _appSettings;
    private ThumbnailListControl? _control;
    private readonly MainViewModel _viewModel;
    private Action<string>? _selectCallback;
    private readonly DatabaseManager _db;
    private bool _isInitialized;

    private void EnsureInitialized()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("ThumbnailLoaderHelper must be initialized before use. Call Initialize() first.");
    }

    // フォルダ読み込み用のキャンセルトークンソース
    private CancellationTokenSource? _folderLoadingCTS;
    // サムネイル読み込み用のキャンセルトークンソース
    private CancellationTokenSource? _thumbnailLoadingCTS;

    public event EventHandler? FileNodesLoaded;

    private volatile bool _isLoading = false;
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
    public ThumbnailLoaderHelper(MainViewModel viewModel, DatabaseManager db, AppSettings appSettings)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _viewModelItems = viewModel.Items ?? throw new ArgumentNullException(nameof(viewModel.Items));

        SortByDate = _appSettings.SortByDate;
        SortAscending = _appSettings.SortAscending;

        // ThumbnailSizeの変更を監視
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ThumbnailSize))
            {
                RefreshThumbnailSizes();
            }
        };
    }

    public void Initialize(ItemsControl thumbnailListBox, Action<string> selectCallback)
    {
        _thumbnailListBox = thumbnailListBox ?? throw new ArgumentNullException(nameof(thumbnailListBox));
        _selectCallback = selectCallback ?? throw new ArgumentNullException(nameof(selectCallback));
        _isInitialized = true;
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
    public async Task LoadFileNodes(string folderPath)
    {
        if (_isLoading) return;
        _isLoading = true;

        var sw = new Stopwatch();
        sw.Start();

        try
        {
            // 前回のフォルダ読み込み処理をキャンセル
            if (_folderLoadingCTS != null)
            {
                Debug.WriteLine($"[CANCEL] 前回のフォルダ読み込み処理をキャンセルします: {_currentFolderPath}");
                _folderLoadingCTS.Cancel();
                _folderLoadingCTS.Dispose();
            }

            // サムネイル読み込み処理もキャンセル
            CancelThumbnailLoading();

            // 新しいキャンセルトークンを作成
            _folderLoadingCTS = new CancellationTokenSource();
            var cancellationToken = _folderLoadingCTS.Token;
            _currentFolderPath = folderPath;

            await Task.Run(async () =>
            {
                try
                {
                    if (!HasFolderAccess(folderPath))
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            _viewModel.Items.ReplaceAll(new List<FileNodeModel>());
                            FileNodesLoaded?.Invoke(this, EventArgs.Empty);
                        });
                        Debug.WriteLine($"[ERROR] Access denied to folder: {folderPath}");
                        return;
                    }

                    // キャンセルされていないか確認
                    cancellationToken.ThrowIfCancellationRequested();

                    // 既存ノードの取得と新規ノードの作成を一括で行う
                    List<FileNodeModel> fileNodes;
                    try
                    {
                        fileNodes = await _db.GetOrCreateFileNodesAsync(folderPath, FileHelper.IsImageFile, cancellationToken);
                        Debug.WriteLine($"[INFO] フォルダ ノード生成まで　'{folderPath}' : {sw.ElapsedMilliseconds} ms");

                        // キャンセルされていないか再確認
                        cancellationToken.ThrowIfCancellationRequested();

                        // ソート条件に従ってノードを並び替え
                        fileNodes = await _db.GetSortedFileNodesAsync(folderPath, SortByDate, SortAscending, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine($"[CANCELLED] DB operation cancelled for folder: {folderPath}");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ERROR] DB operation failed for folder: {folderPath}, Error: {ex.Message}");
                        throw;
                    }

                    // キャンセルされていないか再確認
                    cancellationToken.ThrowIfCancellationRequested();

                    var dummyImage = GetDummyImage();

                    // サムネイル情報を設定 (ロード済みのノードでも状態をリセット)
                    foreach (var node in fileNodes)
                    {
                        node.ThumbnailInfo = new ThumbnailInfo(dummyImage, ThumbnailState.NotLoaded);
                    }

                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        // モデルにノードを設定
                        _viewModel.Items.ReplaceAll(fileNodes);
                        _viewModel.SelectedItems.Clear();

                        // 初期選択を実行する前にUIを更新させる
                        FileNodesLoaded?.Invoke(this, EventArgs.Empty);
                        Debug.WriteLine($"フォルダ ロード時間'{folderPath}' : {sw.ElapsedMilliseconds} ms");

                        // サムネイルの初期ロードを開始（完了を待つ）
                        await LoadInitialThumbnailsAsync();
                    });
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"[CANCELLED] フォルダ読み込みがキャンセルされました: {folderPath}");
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] フォルダ読み込み中にエラーが発生しました: {ex.Message}");
                    throw;
                }
            });
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"[CANCELLED] フォルダ読み込みがキャンセルされました: {folderPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] フォルダ読み込み中にエラーが発生しました: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// 初期表示時に見える範囲のサムネイルをロードします
    /// </summary>
    private async Task LoadInitialThumbnailsAsync()
    {
        try
        {
            // 初期化チェック
            EnsureInitialized();

            // 新しいキャンセルトークンを作成
            InitializeThumbnailLoadingCTS();
            var cancellationToken = GetThumbnailLoadingToken();

            // UIスレッドでの処理を保証
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // 表示範囲のアイテムを取得
                    var scrollViewer = FindVisualChild<ScrollViewer>(_thumbnailListBox);
                    if (scrollViewer == null)
                    {
                        Debug.WriteLine("[WARNING] ScrollViewer not found");
                        return;
                    }

                    // ViewportSizeが0の場合は更新を待つ（最大10回試行）
                    int retryCount = 0;
                    const int maxRetries = 10;
                    const int retryDelay = 50; // ミリ秒

                    while ((scrollViewer.ViewportWidth <= 0 || scrollViewer.ViewportHeight <= 0) && retryCount < maxRetries)
                    {
                        Debug.WriteLine($"[INFO] Waiting for viewport size to be initialized (attempt {retryCount + 1}/{maxRetries})");
                        await Task.Delay(retryDelay, cancellationToken);
                        retryCount++;
                    }

                    // ViewportSizeが取得できない場合はデフォルト値を使用
                    double viewportWidth = scrollViewer.ViewportWidth > 0 ? scrollViewer.ViewportWidth : 800;
                    double viewportHeight = scrollViewer.ViewportHeight > 0 ? scrollViewer.ViewportHeight : 600;

                    // 表示範囲の計算
                    var itemsPerRow = Math.Max(1, CalculateItemsPerRow(viewportWidth));
                    var visibleRows = Math.Max(1, (int)(viewportHeight / (_thumbnailSize + 24)) + 1); // マージンを考慮
                    var bufferSize = itemsPerRow * visibleRows;

                    Debug.WriteLine($"[INFO] ViewportSize: {viewportWidth}x{viewportHeight}, ItemsPerRow: {itemsPerRow}, VisibleRows: {visibleRows}");

                    // 表示範囲のアイテムを処理
                    var nodes = _viewModel.Items.Take(bufferSize).ToArray();
                    if (nodes.Length > 0)
                    {
                        await LoadMoreThumbnailsAsync(0, nodes.Length - 1);
                        Debug.WriteLine($"[INFO] Initial thumbnails loaded: {nodes.Length} items");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] UI thread operation failed: {ex.Message}");
                }
            });
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[CANCELLED] 初期サムネイルのロードがキャンセルされました");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] 初期サムネイルのロード中にエラーが発生しました: {ex.Message}");
        }
    }

    /// <summary>
    /// サムネイルのサイズを更新し、すべてのサムネイルを再生成します
    /// </summary>
    public async void RefreshThumbnailSizes()
    {
        // サムネイル読み込みをキャンセル
        CancelThumbnailLoading();

        // 新しいキャンセルトークンを作成
        InitializeThumbnailLoadingCTS();

        try
        {
            var fileNodes = _viewModel.Items.ToList();

            var dummyImage = GetDummyImage();

            foreach (var node in fileNodes)
            {
                node.ThumbnailInfo = new ThumbnailInfo(dummyImage, ThumbnailState.NotLoaded);
            }

            var scrollViewer = FindVisualChild<ScrollViewer>(_thumbnailListBox);
            if (scrollViewer != null)
            {
                int firstIndex = 0;
                int lastIndex = Math.Min(20, fileNodes.Count - 1);

                await LoadMoreThumbnailsAsync(firstIndex, lastIndex);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] サムネイルサイズ更新中にエラーが発生しました: {ex.Message}");
        }
    }

    private BitmapSource ResizeThumbnail(BitmapSource original, int width, int height)
    {
        var scale = Math.Min(
            width / (double)original.PixelWidth,
            height / (double)original.PixelHeight
        );

        var newWidth = (int)(original.PixelWidth * scale);
        var newHeight = (int)(original.PixelHeight * scale);

        var resized = new TransformedBitmap(original, new ScaleTransform(scale, scale));
        var cropped = new CroppedBitmap(resized, new Int32Rect(0, 0, newWidth, newHeight));

        return cropped;
    }

    /// <summary>
    /// 表示されている範囲のサムネイルを読み込みます（仮想化対応版）
    /// </summary>
    public async Task LoadMoreThumbnailsAsync(int startIndex, int endIndex)
    {
        // 全画面表示中はサムネイル生成をスキップ
        if (_isFullscreenMode)
        {
            return;
        }

        try
        {
            // 既に読み込み済みのサムネイルは再度読み込まない
            var nodes = _viewModel.Items.Skip(startIndex).Take(endIndex - startIndex + 1)
                .Where(node => node.ThumbnailInfo?.State == ThumbnailState.NotLoaded)
                .ToArray();

            if (nodes.Length == 0)
            {
                return;
            }

            Debug.WriteLine($"[INFO] Loading thumbnails from index {startIndex} to {endIndex} (total: {nodes.Length} items)");

            // バッチサイズの計算（論理プロセッサ数を基準に、最大8まで）
            int batchSize = Math.Min(8, Environment.ProcessorCount);

            // 既存のトークンを使用するか、新しいトークンを作成
            var cancellationToken = _thumbnailLoadingCTS?.Token ?? CancellationToken.None;
            if (cancellationToken.IsCancellationRequested)
            {
                InitializeThumbnailLoadingCTS();
                cancellationToken = GetThumbnailLoadingToken();
            }

            // バッチ処理を実行
            for (int i = 0; i < nodes.Length; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int currentBatchSize = Math.Min(batchSize, nodes.Length - i);
                await ProcessBatchAsync(nodes, i, i + currentBatchSize - 1, cancellationToken);

                // UIの応答性を維持するため、短い遅延を入れる
                if (i + currentBatchSize < nodes.Length)
                {
                    await Task.Delay(10, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[CANCELLED] サムネイル読み込みがキャンセルされました");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] サムネイル読み込み中にエラーが発生しました: {ex.Message}");
        }
    }

    private async Task ProcessBatchAsync(FileNodeModel[] nodes, int startIndex, int endIndex, CancellationToken cancellationToken)
    {
        try
        {
            if (nodes == null || nodes.Length == 0)
            {
                Debug.WriteLine("[WARNING] No nodes to process in batch");
                return;
            }

            for (int i = startIndex; i <= endIndex && i < nodes.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var node = nodes[i];
                if (node == null || node.ThumbnailInfo?.State != ThumbnailState.NotLoaded)
                {
                    continue;
                }

                try
                {
                    var thumbnailInfo = GetOrCreateThumbnail(node.FullPath, cancellationToken);
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        node.ThumbnailInfo = thumbnailInfo;
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] サムネイル生成中にエラーが発生しました '{node.FullPath}': {ex.Message}");
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        node.ThumbnailInfo = new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] サムネイル処理中にエラーが発生しました: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// サムネイル読み込み用のキャンセルトークンを初期化します
    /// </summary>
    private void InitializeThumbnailLoadingCTS()
    {
        if (_thumbnailLoadingCTS != null)
        {
            _thumbnailLoadingCTS.Dispose();
        }
        _thumbnailLoadingCTS = new CancellationTokenSource();
    }

    /// <summary>
    /// サムネイル読み込み用のキャンセルトークンを取得します
    /// </summary>
    public CancellationToken GetThumbnailLoadingToken()
    {
        return _thumbnailLoadingCTS?.Token ?? CancellationToken.None;
    }

    /// <summary>
    /// フォルダ読み込み用のキャンセルトークンを取得します
    /// </summary>
    public CancellationToken GetFolderLoadingToken()
    {
        return _folderLoadingCTS?.Token ?? CancellationToken.None;
    }

    /// <summary>
    /// 現在のキャンセルトークンを取得します（後方互換性のため）
    /// </summary>
    public CancellationToken GetCurrentCancellationToken()
    {
        // 優先順位: フォルダ読み込み > サムネイル読み込み > なし
        if (_folderLoadingCTS != null && !_folderLoadingCTS.IsCancellationRequested)
            return _folderLoadingCTS.Token;
        if (_thumbnailLoadingCTS != null && !_thumbnailLoadingCTS.IsCancellationRequested)
            return _thumbnailLoadingCTS.Token;
        return CancellationToken.None;
    }

    /// <summary>
    /// サムネイル読み込みをキャンセルします
    /// </summary>
    public void CancelThumbnailLoading()
    {
        if (_thumbnailLoadingCTS != null)
        {
            try
            {
                _thumbnailLoadingCTS.Cancel();
                Debug.WriteLine($"[CANCEL] サムネイル読み込み処理をキャンセルしました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] サムネイル読み込みキャンセル処理中にエラーが発生しました: {ex.Message}");
            }
            finally
            {
                _thumbnailLoadingCTS.Dispose();
                _thumbnailLoadingCTS = null;
            }
        }
    }

    /// <summary>
    /// フォルダ読み込みをキャンセルします
    /// </summary>
    public void CancelFolderLoading()
    {
        if (_folderLoadingCTS != null)
        {
            try
            {
                _folderLoadingCTS.Cancel();
                Debug.WriteLine($"[CANCEL] フォルダ読み込み処理をキャンセルしました: {_currentFolderPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] フォルダ読み込みキャンセル処理中にエラーが発生しました: {ex.Message}");
            }
            finally
            {
                _folderLoadingCTS.Dispose();
                _folderLoadingCTS = null;
            }
        }
    }

    /// <summary>
    /// すべての読み込み処理をキャンセルします
    /// </summary>
    public void CancelAllLoading()
    {
        CancelFolderLoading();
        CancelThumbnailLoading();
    }

    private ThumbnailInfo GetOrCreateThumbnail(string? imagePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            return new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
        }

        try
        {
            // キャンセルされていないか確認
            cancellationToken.ThrowIfCancellationRequested();

            // 画像ファイルを読み込む
            using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            // キャンセルされていないか確認
            cancellationToken.ThrowIfCancellationRequested();

            // サムネイルサイズに合わせてリサイズ
            var thumbnail = ResizeThumbnail(decoder.Frames[0], _thumbnailSize, _thumbnailSize);

            // キャンセルされていないか確認
            cancellationToken.ThrowIfCancellationRequested();

            // サムネイルをフリーズして、UIスレッドでの使用を最適化
            thumbnail.Freeze();

            return new ThumbnailInfo(thumbnail, ThumbnailState.Loaded);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"[CANCELLED] サムネイル生成がキャンセルされました: {imagePath}");
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] サムネイル生成中にエラーが発生しました '{imagePath}': {ex.Message}");
            return new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
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

            drawingContext.DrawLine(new Pen(Brushes.Silver, 1), new Point(width / 2 - 10, height / 2), new Point(width / 2 + 10, height / 2));
            drawingContext.DrawLine(new Pen(Brushes.Silver, 1), new Point(width / 2, height / 2 - 10), new Point(width / 2, height / 2 + 10));
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
            var fileNode = await _db.CreateFileNodeAsync(path);
            if (fileNode != null)
            {
                fileNode.ThumbnailInfo = new ThumbnailInfo(GetDummyImage(), ThumbnailState.NotLoaded);
            }

            return fileNode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ファイルノード作成中にエラーが発生しました: {ex.Message}");
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
}

