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

/// <summary>
/// サムネイルの読み込みと管理を行うヘルパークラス
/// </summary>
public class ThumbnailLoaderHelper
{
    private static BitmapSource? _commonDummyImage;
    private static BitmapSource? _commonErrorImage;
    private static readonly object _staticLock = new object();

    private CancellationTokenSource? _cancellationTokenSource;
    private string _currentFolderPath = string.Empty;
    private readonly ItemsControl _thumbnailListBox;
    private int _thumbnailSize = 120;
    private ObservableCollection<FileNodeModel> _viewModelItems;
    private AppSettings _appSettings;
    private readonly ThumbnailListControl _control;
    private readonly MainViewModel _viewModel;
    private readonly Action<string> _selectCallback;
    private readonly DatabaseManager _db = new();

    public event EventHandler? FileNodesLoaded;

    private volatile bool _isLoading = false;

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
    public ThumbnailLoaderHelper(ItemsControl thumbnailListBox, Action<string> selectCallback, ThumbnailListControl control, MainViewModel viewModel)
    {
        _thumbnailListBox = thumbnailListBox ?? throw new ArgumentNullException(nameof(thumbnailListBox));
        _selectCallback = selectCallback;
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _viewModelItems = viewModel.Items ?? throw new ArgumentNullException(nameof(viewModel.Items));

        // 設定を読み込む
        _appSettings = SettingsHelper.GetSettings();
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
    /// 指定されたフォルダのサムネイルを読み込みます（最適化版）
    /// </summary>
    /// <param name="folderPath">サムネイルを読み込むフォルダのパス</param>
    public async void LoadFileNodes(string folderPath)
    {
        if (_isLoading) return;
        _isLoading = true;

        var sw = new Stopwatch();
        sw.Start();

        try
        {
            Debug.WriteLine("Start loading file nodes");

            CancelAllLoading();
            _cancellationTokenSource = new CancellationTokenSource();
            _currentFolderPath = folderPath;

            if (!HasFolderAccess(folderPath))
            {
                _viewModel.Items.ReplaceAll(new List<FileNodeModel>());
                FileNodesLoaded?.Invoke(this, EventArgs.Empty);
                MessageBox.Show($"フォルダ '{folderPath}' へのアクセスが拒否されました。", "アクセスエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 先にUIを空にして、ユーザーに反応を示す
            _viewModel.Items.ReplaceAll(new List<FileNodeModel>());

            Debug.WriteLine($"UI cleared: {sw.ElapsedMilliseconds}ms");

            // 別スレッドでファイル列挙を行う
            var imageFilePaths = await Task.Run(() =>
                Directory.EnumerateFiles(folderPath)
                    .Where(IsImageFile)
                    .ToList());

            Debug.WriteLine($"画像ファイル列挙: {sw.ElapsedMilliseconds}ms, {imageFilePaths.Count}件");

            // 既存ノードの取得と新規ノードの作成を一括で行う
            var fileNodes = await _db.GetOrCreateFileNodesAsync(folderPath, imageFilePaths);
            var dummyImage = GetDummyImage();

            Debug.WriteLine($"DBからのノード取得と作成: {sw.ElapsedMilliseconds}ms");

            // サムネイル情報を設定 (ロード済みのノードでも状態をリセット)
            foreach (var node in fileNodes)
            {
                node.ThumbnailInfo = new ThumbnailInfo(dummyImage, ThumbnailState.NotLoaded);
            }

            Debug.WriteLine($"サムネイル情報設定: {sw.ElapsedMilliseconds}ms");

            // 並べ替え（メモリ内で処理）
            if (_appSettings.SortByDate)
            {
                fileNodes = _appSettings.SortAscending ?
                    fileNodes.OrderBy(fn => fn.CreationTime).ToList() :
                    fileNodes.OrderByDescending(fn => fn.CreationTime).ToList();
            }
            else
            {
                fileNodes = _appSettings.SortAscending ?
                    fileNodes.OrderBy(fn => fn.Name).ToList() :
                    fileNodes.OrderByDescending(fn => fn.Name).ToList();
            }

            Debug.WriteLine($"並べ替え: {sw.ElapsedMilliseconds}ms");

            // UIへの一括更新
            _viewModel.Items.ReplaceAll(fileNodes);
            _viewModel.SelectedItem = null;

            Debug.WriteLine($"UIへのデータセット: {sw.ElapsedMilliseconds}ms");

            // サムネイルロードイベント発行前に初期表示される分のサムネイルをロード
            await Task.Delay(100); // UIの更新を待つ
            await LoadInitialThumbnailsAsync();

            Debug.WriteLine($"初期サムネイルロード: {sw.ElapsedMilliseconds}ms");

            // イベント発行
            FileNodesLoaded?.Invoke(this, EventArgs.Empty);

            Debug.WriteLine($"全体の処理時間: {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"フォルダ '{folderPath}' の処理中にエラーが発生しました: {ex.Message}");
            MessageBox.Show($"フォルダ '{folderPath}' の処理中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (_viewModel.Items.Count == 0) return;

            var scrollViewer = FindVisualChild<ScrollViewer>(_thumbnailListBox);
            if (scrollViewer != null)
            {
                // 最初の画面に表示される項目のみロード（仮想化されている場合は20個程度）
                var startIndex = 0;
                var endIndex = Math.Min(20, _viewModel.Items.Count - 1);

                await LoadMoreThumbnailsAsync(startIndex, endIndex);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"初期サムネイルのロード中にエラーが発生しました: {ex.Message}");
        }
    }

    /// <summary>
    /// サムネイルのサイズを更新し、すべてのサムネイルを再生成します
    /// </summary>
    public async void RefreshThumbnailSizes()
    {
        CancelAllLoading();
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var fileNodes = _viewModel.Items.ToList();

            var selectedItem = _viewModel.SelectedItem;

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
            Debug.WriteLine($"サムネイルサイズ更新中にエラーが発生しました: {ex.Message}");
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
    /// 表示されている範囲のサムネイルを読み込みます（最適化版）
    /// </summary>
    public async Task LoadMoreThumbnailsAsync(int startIndex, int endIndex)
    {
        var cancellationToken = GetCurrentCancellationToken();
        var nodes = _viewModel.Items.ToArray();

        // インデックスの範囲チェック
        startIndex = Math.Max(0, startIndex);
        endIndex = Math.Min(nodes.Length - 1, endIndex);

        if (startIndex > endIndex || startIndex < 0) return;

        // より効率的なバッチサイズ（適宜調整可能）
        int batchSize = 8;
        var tasks = new List<Task>();

        for (int batchStart = startIndex; batchStart <= endIndex; batchStart += batchSize)
        {
            if (cancellationToken.IsCancellationRequested) return;

            int batchEnd = Math.Min(batchStart + batchSize - 1, endIndex);

            // 非同期でバッチ処理を実行
            var task = ProcessBatchAsync(nodes, batchStart, batchEnd, cancellationToken);
            tasks.Add(task);

            // UIがフリーズしないように少し待つ
            await Task.Delay(5, CancellationToken.None);
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // キャンセルされた場合は無視
        }
    }

    private async Task ProcessBatchAsync(FileNodeModel[] nodes, int startIndex, int endIndex, CancellationToken cancellationToken)
    {
        for (int i = startIndex; i <= endIndex; i++)
        {
            if (cancellationToken.IsCancellationRequested) return;

            var fileNode = nodes[i];
            if (fileNode != null &&
                (fileNode.ThumbnailInfo == null ||
                 fileNode.ThumbnailInfo.State != ThumbnailState.Loaded))
            {
                var thumbnailInfo = await Task.Run(() => GetOrCreateThumbnail(fileNode.FullPath, cancellationToken), cancellationToken);

                // UIスレッドにポストする必要はない（プロパティ変更通知が自動的に処理）
                if (!cancellationToken.IsCancellationRequested)
                {
                    fileNode.ThumbnailInfo = thumbnailInfo;
                }
            }
        }
    }

    public CancellationToken GetCurrentCancellationToken()
    {
        return _cancellationTokenSource?.Token ?? CancellationToken.None;
    }

    public void CancelAllLoading()
    {
        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
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

            var thumbnail = ThumbnailHelper.CreateThumbnail(imagePath, _thumbnailSize - 2, _thumbnailSize - 2, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            return new ThumbnailInfo(thumbnail, ThumbnailState.Loaded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            var errorImage = GetErrorImage();
            return new ThumbnailInfo(errorImage, ThumbnailState.Error);
        }
    }

    static private bool IsImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension == ".jpg" || extension == ".jpeg" || extension == ".png" ||
               extension == ".gif" || extension == ".bmp" || extension == ".webp";
    }

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
            drawingContext.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, width, height));

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
