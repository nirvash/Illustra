using Illustra.Views;
using Illustra.ViewModels;

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

    private CancellationTokenSource? _cancellationTokenSource;
    private string _currentFolderPath = string.Empty;
    private readonly ItemsControl _thumbnailListBox;
    private int _thumbnailSize = 120;
    private ObservableCollection<FileNodeModel> _viewModelItems;
    private AppSettings _appSettings;
    private readonly ThumbnailListControl _control;
    private readonly MainViewModel _viewModel;

    /// <summary>
    /// サムネイルローダーを初期化します
    /// </summary>
    /// <param name="thumbnailListBox">サムネイルを表示するItemsControl</param>
    /// <param name="onThumbnailClick">サムネイルクリック時のアクション</param>
    /// <param name="control">MainWindowの参照</param>
    /// <param name="viewModel">MainViewModelの参照</param>
    public ThumbnailLoaderHelper(ItemsControl thumbnailListBox, Action<string> onThumbnailClick, ThumbnailListControl control, MainViewModel viewModel)
    {
        _thumbnailListBox = thumbnailListBox ?? throw new ArgumentNullException(nameof(thumbnailListBox));
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
    public void LoadFileNodes(string folderPath)
    {
        // 既存のサムネイル読み込み処理をキャンセル
        CancelAllLoading();
        _cancellationTokenSource = new CancellationTokenSource();

        // フォルダアクセスのチェック
        if (!HasFolderAccess(folderPath))
        {
            // アクセス権限がない場合、空のリストを設定
            _viewModel.Items.ReplaceAll(new List<FileNodeModel>());
            fileNodesLoaded?.Invoke(this, EventArgs.Empty);
            // ユーザーに通知（オプション）
            MessageBox.Show($"フォルダ '{folderPath}' へのアクセスが拒否されました。", "アクセスエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // 画像ファイルのみを収集
            var imageFilePaths = Directory.EnumerateFiles(folderPath)
                .Where(IsImageFile)
                .ToList();

            // FileNodeModelのリストを初期化
            var fileNodes = new List<FileNodeModel>(imageFilePaths.Count);

            // ダミー画像を1回だけ取得
            var dummyImage = GetDummyImage();

            // FileNodeModelをプリロード（画像ファイルだけを対象に）
            foreach (var filePath in imageFilePaths)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var fileNode = new FileNodeModel(filePath, new ThumbnailInfo(dummyImage, ThumbnailState.NotLoaded))
                    {
                        CreationTime = fileInfo.CreationTime
                    };
                    fileNodes.Add(fileNode);
                }
                catch (UnauthorizedAccessException)
                {
                    // 個別ファイルへのアクセスエラーは無視
                    continue;
                }
                catch (Exception ex)
                {
                    // その他の例外もログ出力のみにして続行
                    Debug.WriteLine($"ファイル '{filePath}' の処理中にエラーが発生しました: {ex.Message}");
                    continue;
                }
            }

            // ソート順の設定に従ってソート
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

            // ViewModelのItemsをクリアして新しいアイテムを設定
            _viewModel.Items.ReplaceAll(fileNodes);

            // ファイルノードのロード完了イベントを発火
            fileNodesLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // フォルダ全体の処理中にエラーが発生した場合
            Debug.WriteLine($"フォルダ '{folderPath}' の処理中にエラーが発生しました: {ex.Message}");
            // 必要に応じてユーザーに通知（オプション）
            MessageBox.Show($"フォルダ '{folderPath}' の処理中にエラーが発生しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// サムネイルを更新するメソッドを修正
    /// </summary>
    public async Task LoadMoreThumbnailsAsync(int startIndex, int endIndex)
    {
        // 現在のキャンセルトークンを取得
        var cancellationToken = GetCurrentCancellationToken();

        var fileNodes = _viewModel.Items.ToList();
        // 範囲チェック
        startIndex = Math.Max(0, startIndex);
        endIndex = Math.Min(fileNodes.Count - 1, endIndex);

        if (startIndex > endIndex || startIndex < 0 || endIndex >= fileNodes.Count) return;

        // 効率的に処理するためのバッチサイズ
        int batchSize = 4;

        // 各バッチ処理をリストに収集
        var tasks = new List<Task>();

        for (int batchStart = startIndex; batchStart <= endIndex; batchStart += batchSize)
        {
            // このバッチの最後のインデックスを計算
            int batchEnd = Math.Min(batchStart + batchSize - 1, endIndex);

            // キャンセルされていないか確認
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var task = ProcessBatchAsync(fileNodes, batchStart, batchEnd, cancellationToken);
            tasks.Add(task);

            // 負荷分散のために少しディレイを入れる
            await Task.Delay(10, CancellationToken.None);
        }

        // すべてのバッチが完了するのを待つ（キャンセル時は例外をキャッチ）
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // キャンセルされた場合は何もしない
        }
    }

    /// <summary>
    /// サムネイル読み込みをバッチ処理する内部メソッド
    /// </summary>
    private async Task ProcessBatchAsync(List<FileNodeModel> fileNodes, int startIndex, int endIndex, CancellationToken cancellationToken)
    {
        for (int i = startIndex; i <= endIndex; i++)
        {
            // キャンセルされていないか確認
            if (cancellationToken.IsCancellationRequested) return;

            var fileNode = fileNodes[i];

            // キャンセルされていないか確認
            if (cancellationToken.IsCancellationRequested) return;

            // サムネイルがロードされていない場合のみ処理
            if (fileNode != null &&
                (fileNode.ThumbnailInfo == null ||
                 fileNode.ThumbnailInfo.State != ThumbnailState.Loaded))
            {
                var thumbnailInfo = await Task.Run(() => GetOrCreateThumbnail(fileNode.FullPath, cancellationToken));
                fileNode.ThumbnailInfo = thumbnailInfo;
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

    private ThumbnailInfo GetOrCreateThumbnail(string? imagePath, CancellationToken cancellationToken)
    {
        if (imagePath == null)
        {
            return new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
        }

        try
        {
            // キャンセルされていないか確認
            cancellationToken.ThrowIfCancellationRequested();

            // ThumbnailHelperのメソッドを呼び出す
            var thumbnail = ThumbnailHelper.CreateThumbnail(imagePath, _thumbnailSize - 2, _thumbnailSize - 2, cancellationToken);

            // 処理後にもキャンセルされていないか確認
            cancellationToken.ThrowIfCancellationRequested();

            return new ThumbnailInfo(thumbnail, ThumbnailState.Loaded);
        }
        catch (OperationCanceledException)
        {
            // キャンセルされた場合は例外を上位に伝播させる
            throw;
        }
        catch (Exception)
        {
            // エラーが発生した場合はエラー画像を返す
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

    static private BitmapSource GenerateDummyImage(int width, int height)
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
