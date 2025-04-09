using Illustra.Views;
using Illustra.ViewModels;
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

namespace Illustra.Helpers;

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
    private AppSettingsModel _appSettings;
    private readonly ThumbnailListControl _control;
    private readonly ThumbnailListViewModel _viewModel;
    private readonly Action<string, bool> _selectCallback;
    private readonly DatabaseManager _db;
    private CancellationTokenSource? _folderLoadingCTS;
    private CancellationTokenSource? _thumbnailLoadCts;
    private readonly ThumbnailRequestQueue _requestQueue;
    private readonly IThumbnailProcessorService _thumbnailProcessor;
    public event EventHandler<ScrollToItemRequestEventArgs>? ScrollToItemRequested;
    public event EventHandler<ThumbnailLoadEventArgs>? ThumbnailsLoaded;

    /// <summary>
    /// 現在読み込み中または読み込み済みのフォルダパスを取得します
    /// </summary>
    public string CurrentFolderPath => _currentFolderPath;

    private volatile bool _isFullscreenMode = false;

    private bool _sortByDate = true;
    public bool SortByDate
    {
        get => _sortByDate;
        set => _sortByDate = value;
    }

    private bool _sortAscending = true;
    public bool SortAscending
    {
        get => _sortAscending;
        set => _sortAscending = value;
    }

    /// <summary>
    /// 全画面表示モードを設定します
    /// </summary>
    public void SetFullscreenMode(bool isFullscreen)
    {
        _isFullscreenMode = isFullscreen;
        LogHelper.LogWithTimestamp(
            $"フルスクリーンモード {(isFullscreen ? "開始" : "終了")} - サムネイル生成を{(isFullscreen ? "抑制" : "再開")}します",
            LogHelper.Categories.ThumbnailLoader);
    }

    /// <summary>
    /// フルスクリーンモードかつウィンドウが覆われている場合にサムネイル読み込みを抑制すべきかどうかを判定します。
    /// </summary>
    /// <returns>抑制すべき場合は true、そうでない場合は false。</returns>
    private async Task<bool> ShouldSuppressThumbnailLoadingAsync()
    {
        if (!_isFullscreenMode)
        {
            return false; // フルスクリーンモードでなければ抑制しない
        }

        // UI スレッドでウィンドウの状態を確認
        bool isCovered = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var parentWindow = Window.GetWindow(_control);
            // parentWindow が null でないこと、かつ覆われていることを確認
            return parentWindow != null && WindowVisibilityChecker.IsWindowCovered(parentWindow);
        });

        // ログ出力はUIスレッドでなくても良い場合が多いが、念のためDispatcher外で行う
        if (isCovered)
        {
            LogHelper.LogWithTimestamp("フルスクリーンモードでウィンドウが覆われているため、サムネイル処理を抑制します", LogHelper.Categories.ThumbnailLoader);
        }
        else
        {
            LogHelper.LogWithTimestamp("フルスクリーンモードですがウィンドウが表示されているため、サムネイル処理を続行します", LogHelper.Categories.ThumbnailLoader);
        }

        return isCovered;
    }


    /// <summary>
    /// スクロールタイプを設定します
    /// </summary>
    public void SetScrollType(ScrollType scrollType)
    {
        _requestQueue.SetScrollType(scrollType);
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
        Action<string, bool> selectCallback,
        ThumbnailListControl control,
        ThumbnailListViewModel viewModel,
        DatabaseManager db,
        IThumbnailProcessorService thumbnailProcessor)
    {
        _thumbnailListBox = thumbnailListBox ?? throw new ArgumentNullException(nameof(thumbnailListBox));
        _selectCallback = selectCallback;
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _thumbnailProcessor = thumbnailProcessor ?? throw new ArgumentNullException(nameof(thumbnailProcessor));
        _requestQueue = new ThumbnailRequestQueue(_thumbnailProcessor);

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
    public async Task LoadFileNodesAsync(string folderPath)
    {
        try
        {
            // 内部でのみトークンを管理
            if (_folderLoadingCTS != null)
            {
                _folderLoadingCTS.Cancel();
                _folderLoadingCTS.Dispose();
            }
            _folderLoadingCTS = new CancellationTokenSource();
            var token = _folderLoadingCTS.Token;

            LogHelper.LogWithTimestamp($"[THUMBNAIL_LOADER] フォルダ読み込み開始: {folderPath}", LogHelper.Categories.ThumbnailLoader);

            // 既存のアイテムをクリア
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _viewModel.Items.Clear();
                _viewModel.FilteredItems.Refresh();
            }, DispatcherPriority.Send);

            // ファイル一覧を取得
            LogHelper.LogWithTimestamp("[PERFORMANCE] ファイル一覧の取得を開始", LogHelper.Categories.ThumbnailLoader);
            var sw = Stopwatch.StartNew();
            var files = await Task.Run(() => GetFilesInFolder(folderPath), token);
            sw.Stop();
            LogHelper.LogWithTimestamp($"[PERFORMANCE] [完了] ファイル取得: {sw.ElapsedMilliseconds}ms - {files.Count}件のファイルを取得", LogHelper.Categories.ThumbnailLoader);

            // ファイルノードを作成
            LogHelper.LogWithTimestamp("ファイルノードの作成を開始", LogHelper.Categories.ThumbnailLoader);
            LogHelper.LogWithTimestamp($"{files.Count}件のファイルからノードを作成します", LogHelper.Categories.ThumbnailLoader);
            var fileNodes = CreateFileNodes(files, token);

            // データベースから情報を取得してノードを強化
            LogHelper.LogWithTimestamp($"{fileNodes.Count}件のファイルのメタデータをバルク取得します", LogHelper.Categories.ThumbnailLoader);
            LogHelper.LogWithTimestamp($"フォルダパス: {folderPath}", LogHelper.Categories.ThumbnailLoader);
            await EnrichFileNodesWithDatabaseInfoBulk(fileNodes, folderPath);
            LogHelper.LogWithTimestamp($"{fileNodes.Count}件のノードをDBから取得しました", LogHelper.Categories.ThumbnailLoader);
            LogHelper.LogWithTimestamp($"{fileNodes.Count}件のノードにDBの情報を設定しました", LogHelper.Categories.ThumbnailLoader);
            LogHelper.LogWithTimestamp($"{fileNodes.Count}件のノードを作成しました", LogHelper.Categories.ThumbnailLoader);

            // レーティングが設定されたときにノードを更新するのでここでのDB更新は不要になった

            // ソート条件に基づいてノードをソート
            if (fileNodes.Count > 0)
            {
                SortFileNodes(fileNodes);
            }

            // ViewModelのItemsを更新
            LogHelper.LogWithTimestamp($"ViewModelのItemsを更新開始: {fileNodes.Count}件", LogHelper.Categories.ThumbnailLoader);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _viewModel.Items.Clear();
                foreach (var node in fileNodes)
                {
                    _viewModel.Items.Add(node);
                }
                _viewModel.FilteredItems.Refresh();

                // 重要: FilteredItemsの件数を確認してログ出力
                int filteredCount = _viewModel.FilteredItems.Cast<FileNodeModel>().Count();
                LogHelper.LogWithTimestamp($"FilteredItemsの件数: {filteredCount}件", LogHelper.Categories.ThumbnailLoader);
            }, DispatcherPriority.Send);

            // 現在のフォルダパスを更新
            _currentFolderPath = folderPath;

            // FileNodesLoaded イベントは不要になったため削除

            LogHelper.LogWithTimestamp("完了", LogHelper.Categories.ThumbnailLoader);
        }
        catch (OperationCanceledException)
        {
            LogHelper.LogWithTimestamp("キャンセルされました", LogHelper.Categories.ThumbnailLoader);
        }
        catch (Exception ex)
        {
            LogHelper.LogError($"ファイルノードの読み込み中にエラーが発生しました: {ex.Message}", ex);
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
            var fileNodes = _viewModel.Items.Cast<FileNodeModel>().ToList();
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
    public async Task LoadMoreThumbnailsAsync(int startIndex, int endIndex, CancellationToken cancellationToken, bool isHighPriority = false)
    {
        var tcs = new TaskCompletionSource<bool>();

        // 新しいリクエストを作成
        var request = new ThumbnailRequest(
            startIndex: startIndex,
            endIndex: endIndex,
            isHighPriority: isHighPriority,
            cancellationToken: cancellationToken,
            completionCallback: (req, success) =>
            {
                // 完了時のコールバック
                if (success)
                {
                    tcs.TrySetResult(true);
                }
                else
                {
                    tcs.TrySetResult(false); // または例外情報があればTrySetExceptionも可能
                }

                // 必要に応じて追加の処理（UI更新通知など）
                OnThumbnailsLoaded(startIndex, endIndex, success);
            }
        );

        // リクエストをキューに追加
        _requestQueue.EnqueueRequest(request);

        try
        {
            await tcs.Task;
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
        _requestQueue.ClearQueue();
        // 必要に応じて現在処理中のリクエストをキャンセルする処理を追加
    }

    private void OnThumbnailsLoaded(int startIndex, int endIndex, bool success)
    {
        ThumbnailsLoaded?.Invoke(this, new ThumbnailLoadEventArgs(startIndex, endIndex, success));
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
            var fileNode = await _db.GetFileNodeAsync(path);
            if (fileNode == null)
            {
                fileNode = _db.CreateFileNode(path); // CS1061 Fix: Use renamed synchronous method
            }

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
                    // fileNode.HasThumbnail = false; // CS1656 Fix: Property is read-only, derived from ThumbnailInfo.State
                    // fileNode.IsLoadingThumbnail = false; // CS1656 Fix: Property is read-only, derived from ThumbnailInfo.State
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
                    // fileNode.HasThumbnail = false; // CS1656 Fix: Property is read-only, derived from ThumbnailInfo.State
                    // fileNode.IsLoadingThumbnail = false; // CS1656 Fix: Property is read-only, derived from ThumbnailInfo.State
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
    /// 指定されたFileNodeModelのサムネイルを非同期で作成します
    /// </summary>
    public async Task CreateThumbnailAsync(int index, CancellationToken cancellationToken)
    {
        var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
        if (index < 0 || index >= filteredItems.Count)
            return;

        var fileNode = filteredItems[index];
        if (fileNode == null)
            return;

        // 既に処理中または処理済み、または試行回数上限の場合はスキップ
        if (fileNode.ThumbnailInfo.IsLoadingThumbnail || fileNode.ThumbnailInfo.HasThumbnail)
            return;

        // 試行回数上限チェック (5回)
        const int MaxThumbnailAttempts = 5;
        if (fileNode.ThumbnailAttemptCount >= MaxThumbnailAttempts)
        {
            LogHelper.LogWithTimestamp($"サムネイル作成試行回数上限スキップ: [{index}]{Path.GetFileName(fileNode.FullPath)} ({fileNode.ThumbnailAttemptCount}回)", LogHelper.Categories.ThumbnailLoader);
            // エラー状態にしておく（再試行されないように）
            if (fileNode.ThumbnailInfo.State != ThumbnailState.Error)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    fileNode.ThumbnailInfo = new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
                }, DispatcherPriority.Send);
            }
            return;
        }

        // フルスクリーンモードでウィンドウが覆われている場合は処理をスキップ
        if (await ShouldSuppressThumbnailLoadingAsync())
        {
            // ログは ShouldSuppressThumbnailLoadingAsync 内で出力される
            return;
        }

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
                // 動画ファイルかどうかをチェック
                bool isVideo = FileHelper.IsVideoFile(fileNode.FullPath);

                // サムネイル生成とアニメーション判定を直接awaitで実行 (Task.Runを削除)
                cancellationToken.ThrowIfCancellationRequested();

                if (isVideo)
                {
                    fileNode.IsVideo = true;  // 動画ファイルはIsVideoをtrue
                    // CreateThumbnailAsyncを直接await
                    thumbnail = await ThumbnailHelper.CreateThumbnailAsync(fileNode.FullPath, ThumbnailSize, ThumbnailSize, cancellationToken);
                }
                else
                {
                    // WebPファイルで500KB以上の場合はアニメーション判定
                    if (Path.GetExtension(fileNode.FullPath).ToLowerInvariant() == ".webp" &&
                        new FileInfo(fileNode.FullPath).Length >= 512000) // 500KB = 512000バイト
                    {
                        var isAnim = await WebPHelper.IsAnimatedWebPAsync(fileNode.FullPath);
                        if (isAnim)
                        {
                            fileNode.IsVideo = true;  // アニメーションWebPはIsVideoをtrue
                        }
                    }
                    // 通常の画像はCreateThumbnailOptimizedAsyncを直接await
                    thumbnail = await ThumbnailHelper.CreateThumbnailOptimizedAsync(fileNode.FullPath, ThumbnailSize, ThumbnailSize, cancellationToken);
                }

                LogHelper.LogWithTimestamp($"画像処理完了: [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);
            }
            catch (OperationCanceledException)
            {
                LogHelper.LogWithTimestamp($"キャンセル: [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);
                throw;
            }
            catch (Exception ex)
            {
                // エラーの種類をログに出力
                LogHelper.LogError($"サムネイル作成エラー [{index}]{fileName}: {ex.GetType().Name} - {ex.Message}", ex);
                thumbnail = GetErrorImage(); // エラー画像を設定

                // IOException の場合のみ試行回数をインクリメント
                if (ex is IOException)
                {
                    fileNode.ThumbnailAttemptCount++;
                    LogHelper.LogWithTimestamp($"失敗(IOException)、試行回数: {fileNode.ThumbnailAttemptCount} [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);
                }
                else // IOException 以外はリトライしない
                {
                    fileNode.ThumbnailAttemptCount = MaxThumbnailAttempts; // 試行回数を上限にして再試行をスキップ
                    LogHelper.LogWithTimestamp($"失敗({ex.GetType().Name})、再試行不可に設定 [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);
                }
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
                    // 成功したので試行回数をリセット
                    fileNode.ThumbnailAttemptCount = 0;

                    LogHelper.LogWithTimestamp($"完了、試行回数リセット: [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);
                }, DispatcherPriority.Send);
            }
            else
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // エラー状態を設定
                    fileNode.ThumbnailInfo = new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
                    // HasThumbnail は ThumbnailInfo.State に連動するので不要
                    LogHelper.LogWithTimestamp($"エラー画像設定（失敗時）: [{index}]{fileName}", LogHelper.Categories.ThumbnailLoader);
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

    private List<string> GetFilesInFolder(string folderPath)
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

            // サポートされている拡張子をFileHelperから取得
            var files = new List<string>();
            foreach (var extension in FileHelper.SupportedExtensions)
            {
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

    /// <summary>
    /// 処理キューを使用してサムネイルを読み込みます。
    /// フルスクリーンモードでウィンドウが完全に覆われている場合は読み込みをスキップします。
    /// </summary>
    public async Task LoadThumbnailsWithQueueAsync(int startIndex, int endIndex, CancellationToken cancellationToken, bool isHighPriority = false)
    {
        // フルスクリーンモードでウィンドウが覆われている場合は処理をスキップ
        if (await ShouldSuppressThumbnailLoadingAsync())
        {
            // ログは ShouldSuppressThumbnailLoading 内で出力される
            return;
        }

        var tcs = new TaskCompletionSource<bool>();

        // 新しいリクエストを作成
        var request = new ThumbnailRequest(
            startIndex: startIndex,
            endIndex: endIndex,
            isHighPriority: isHighPriority,
            cancellationToken: cancellationToken,
            completionCallback: (req, success) =>
            {
                // キャンセルされた場合は特別な処理をしない
                if (req.CancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled();
                    return;
                }

                // 完了時のコールバック
                if (success)
                {
                    tcs.TrySetResult(true);
                }
                else
                {
                    tcs.TrySetResult(false);
                }

                // 必要に応じて追加の処理
                LogHelper.LogWithTimestamp($"サムネイル処理完了通知: {req.StartIndex}～{req.EndIndex}, 成功: {success}", LogHelper.Categories.ThumbnailLoader);
            }
        );

        // キャンセルされたリクエストをキューから削除
        _requestQueue.CancelRequests(cancellationToken);

        // リクエストをキューに追加
        _requestQueue.EnqueueRequest(request);

        await tcs.Task;
    }

    /// <summary>
    /// すべてのサムネイルの状態をリセットし、ダミー画像に置き換えます。
    /// </summary>
    public void ResetAllThumbnailStatus()
    {
        var dummyImage = GetDummyImage();
        var fileNodes = _viewModel.Items.Cast<FileNodeModel>().ToList();
        foreach (var node in fileNodes)
        {
            // 状態をNotLoadedにし、画像をダミー画像に設定
            node.ThumbnailInfo = new ThumbnailInfo(dummyImage, ThumbnailState.NotLoaded);
        }
        // キューもクリアする
        _requestQueue.ClearQueue();
        LogHelper.LogWithTimestamp("すべてのサムネイルの状態をリセットしました", LogHelper.Categories.ThumbnailLoader);
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

/// <summary>
/// サムネイルロード完了イベント引数クラス
/// </summary>
public class ThumbnailLoadEventArgs : EventArgs
{
    public int StartIndex { get; }
    public int EndIndex { get; }
    public bool Success { get; }

    public ThumbnailLoadEventArgs(int startIndex, int endIndex, bool success)
    {
        StartIndex = startIndex;
        EndIndex = endIndex;
        Success = success;
    }
}


