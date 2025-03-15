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

/// <summary>
/// サムネイルの読み込みと管理を行うヘルパークラス
/// </summary>
public class ThumbnailLoaderHelper
{
    private static SemaphoreSlim _folderLoadingSemaphore = new SemaphoreSlim(1, 1);
    private static SemaphoreSlim _thumbnailLoadingSemaphore = new SemaphoreSlim(1, 1);
    private static BitmapSource? _commonDummyImage;
    private static BitmapSource? _commonErrorImage;
    private static readonly object _staticLock = new object();

    private string _currentFolderPath = string.Empty;
    private readonly ItemsControl _thumbnailListBox;
    private int _thumbnailSize = 120;
    private ObservableCollection<FileNodeModel> _viewModelItems;
    private AppSettings _appSettings;
    private readonly ThumbnailListControl _control;
    private readonly MainViewModel _viewModel;
    private readonly Action<string> _selectCallback;
    private readonly DatabaseManager _db;

    // フォルダ読み込み用のキャンセルトークンソース
    private CancellationTokenSource? _folderLoadingCTS;
    // サムネイル読み込み用のキャンセルトークンソース
    private CancellationTokenSource? _thumbnailLoadingCTS;

    public event EventHandler? FileNodesLoaded;
    public event EventHandler<ScrollToItemRequestEventArgs>? ScrollToItemRequested;

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
    public async Task LoadFileNodes(string folderPath)
    {
        await _folderLoadingSemaphore.WaitAsync();
        try
        {
            if (_isLoading) return;
            _isLoading = true;

            var sw = new Stopwatch();
            sw.Start();

            try
            {
                Debug.WriteLine($"[INFO] フォルダ読み込み処理を開始します: {folderPath}");
                // 前回のフォルダ読み込み処理をキャンセル
                if (_folderLoadingCTS != null)
                {
                    Debug.WriteLine($"[CANCEL] 前回のフォルダ読み込み処理をキャンセルします: {_currentFolderPath}");
                    _folderLoadingCTS.Cancel();
                    _folderLoadingCTS.Dispose();
                }

                // サムネイル読み込み処理もキャンセル
                Debug.WriteLine($"[CANCEL] フォルダ読み込み処理のためサムネイル読み込み処理をキャンセルします {folderPath}");
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
                            // メモリ上でソートを実行
                            SortHelper.SortFileNodes(fileNodes, SortByDate, SortAscending);
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

                        // UIスレッドでノードを設定 (BulkObservableCollectionの操作は同一スレッド(=UI)のみ)
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            // モデルにノードを設定
                            _viewModel.Items.ReplaceAll(fileNodes);
                            _viewModel.SelectedItems.Clear();

                            // 初期選択を実行する前にUIを更新させる
                            FileNodesLoaded?.Invoke(this, EventArgs.Empty);
                            Debug.WriteLine($"フォルダ ロード時間'{folderPath}' : {sw.ElapsedMilliseconds} ms");
                            _ = LoadInitialThumbnailsAsync();
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine($"[CANCELLED] フォルダ読み込み処理がキャンセルされました: {folderPath}");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ERROR] フォルダ処理中にエラーが発生しました '{folderPath}': {ex.Message}");
                        throw;
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[CANCELLED] フォルダ読み込み処理がキャンセルされました: {folderPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] フォルダ読み込み処理中に予期せぬエラーが発生しました: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }
        finally
        {
            _folderLoadingSemaphore.Release();
        }
    }

    /// <summary>
    /// 初期表示時に見える範囲のサムネイルをロードします
    /// </summary>
    private async Task LoadInitialThumbnailsAsync()
    {
        Debug.WriteLine("[初期サムネイルロード] LoadInitialThumbnailsAsync メソッドが呼ばれました");
        try
        {
            // サムネイル読み込み用の新しいキャンセルトークンを作成
            InitializeThumbnailLoadingCTS();
            var cancellationToken = _thumbnailLoadingCTS?.Token ?? CancellationToken.None;

            // フィルタされたアイテムの個数をチェック
            var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>();
            if (!filteredItems.Any()) return;

            var scrollViewer = FindVisualChild<ScrollViewer>(_thumbnailListBox);
            if (scrollViewer != null)
            {
                // 最初の画面に表示される項目数を計算
                var itemsPerRow = CalculateItemsPerRow(scrollViewer.ViewportWidth);
                var visibleRows = (int)(scrollViewer.ViewportHeight / (_thumbnailSize + 4)); // マージンを考慮
                var visibleItems = itemsPerRow * (visibleRows + 1); // 余裕を持って1行分多めにロード

                var startIndex = 0;
                var endIndex = Math.Min(visibleItems - 1, filteredItems.Count() - 1);

                // Debug.WriteLine($"Initial load: {visibleItems} items ({itemsPerRow} per row, {visibleRows} rows)");
                await LoadMoreThumbnailsAsync(startIndex, endIndex);
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"[CANCELLED] 初期サムネイルのロードがキャンセルされました");
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
    public async Task LoadMoreThumbnailsAsync(int startIndex, int endIndex, CancellationToken cancellationToken = default)
    {
        Debug.WriteLine($"[サムネイルロード] LoadMoreThumbnailsAsync メソッドが呼ばれました: ({startIndex} - {endIndex})");

        // 全画面表示中はサムネイル生成をスキップ
        if (_isFullscreenMode)
        {
            return;
        }

        // 外部から渡されたトークンを使用
        try
        {
            await _thumbnailLoadingSemaphore.WaitAsync(cancellationToken);
            try
            {
                // フィルタされたアイテムのリストを取得（実際に表示されているもの）
                var filteredNodes = _viewModel.FilteredItems.Cast<FileNodeModel>().ToArray();

                // インデックスの範囲チェック
                startIndex = Math.Max(0, startIndex);
                endIndex = Math.Min(filteredNodes.Length - 1, endIndex);

                if (startIndex > endIndex || startIndex < 0) return;

                // 表示範囲のアイテムを優先的に処理
                var priorityNodes = new List<FileNodeModel>();

                for (int i = startIndex; i <= endIndex; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileNode = filteredNodes[i];
                    if (fileNode != null &&
                        (fileNode.ThumbnailInfo == null ||
                         fileNode.ThumbnailInfo.State != ThumbnailState.Loaded))
                    {
                        priorityNodes.Add(fileNode);
                    }
                }

                // 優先ノードを処理
                foreach (var node in priorityNodes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var thumbnailInfo = await Task.Run(() => GetOrCreateThumbnail(node.FullPath, cancellationToken), cancellationToken);
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            node.ThumbnailInfo = thumbnailInfo;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine($"[CANCELLED] サムネイルの作成処理がキャンセルされました: {node.FullPath}");
                        throw; // キャンセル例外を再スロー
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ERROR] サムネイル読み込み中にエラーが発生しました: {ex.Message}");
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            node.ThumbnailInfo = new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
                        }
                    }
                }
            }
            finally
            {
                _thumbnailLoadingSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[CANCELLED] サムネイル読み込み処理がキャンセルされました");
            throw; // キャンセル例外を再スロー
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] サムネイル読み込み中にエラーが発生しました: {ex.Message}");
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
        if (imagePath == null)
        {
            return new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var thumbnail = ThumbnailHelper.CreateThumbnail(imagePath, _thumbnailSize - 2, _thumbnailSize - 2, cancellationToken);

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
        var cancellationToken = GetThumbnailLoadingToken();

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

