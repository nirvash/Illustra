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
    private readonly MainWindow _mainWindow;
    private readonly MainViewModel _viewModel;

    /// <summary>
    /// サムネイルローダーを初期化します
    /// </summary>
    /// <param name="thumbnailListBox">サムネイルを表示するItemsControl</param>
    /// <param name="onThumbnailClick">サムネイルクリック時のアクション</param>
    /// <param name="mainWindow">MainWindowの参照</param>
    /// <param name="viewModel">MainViewModelの参照</param>
    public ThumbnailLoaderHelper(ItemsControl thumbnailListBox, Action<string> onThumbnailClick, MainWindow mainWindow, MainViewModel viewModel)
    {
        _thumbnailListBox = thumbnailListBox ?? throw new ArgumentNullException(nameof(thumbnailListBox));
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _viewModelItems = viewModel.Items ?? throw new ArgumentNullException(nameof(viewModel.Items));
    }

    /// <summary>
    /// 現在のフォルダパスを取得します
    /// </summary>
    public string CurrentFolderPath { get; private set; } = string.Empty;

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
        CurrentFolderPath = folderPath;
        var fileNodes = new List<FileNodeModel>();

        foreach (var filePath in Directory.GetFiles(folderPath))
        {
            if (!IsImageFile(filePath))
                continue;
            var fileNode = new FileNodeModel(filePath, new ThumbnailInfo(GetDummyImage(), ThumbnailState.NotLoaded));
            fileNodes.Add(fileNode);
        }

        // ソート順の設定に従ってソート
        if (_mainWindow.SortByDate)
        {
            fileNodes = _mainWindow.SortAscending ? fileNodes.OrderBy(fn => fn.CreationTime).ToList() : fileNodes.OrderByDescending(fn => fn.CreationTime).ToList();
        }
        else
        {
            fileNodes = _mainWindow.SortAscending ? fileNodes.OrderBy(fn => fn.Name).ToList() : fileNodes.OrderByDescending(fn => fn.Name).ToList();
        }

        // ViewModelのItemsに設定
        _viewModel.Items.ReplaceAll(fileNodes);

        // ファイルノードのロード完了イベントを発火
        fileNodesLoaded?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>
    /// サムネイルを更新するメソッドを修正
    /// </summary>
    public async Task LoadMoreThumbnailsAsync(int startIndex, int endIndex)
    {
        var fileNodes = _viewModel.Items.ToList(); ;
        // 範囲チェック
        startIndex = Math.Max(0, startIndex);
        endIndex = Math.Min(fileNodes.Count - 1, endIndex);

        if (startIndex > endIndex || startIndex < 0 || endIndex >= fileNodes.Count) return;

        for (int i = startIndex; i <= endIndex; i++)
        {
            var fileNode = fileNodes[i];

            // サムネイルがダミー画像の場合またはまだロードされていない場合のみ処理
            if (fileNode != null &&
                (fileNode.ThumbnailInfo == null ||
                 fileNode.ThumbnailInfo.State != ThumbnailState.Loaded))
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
        if (imagePath == null)
        {
            return new ThumbnailInfo(GetErrorImage(), ThumbnailState.Error);
        }

        try
        {
            var thumbnail = await ThumbnailHelper.CreateThumbnailAsync(imagePath, _thumbnailSize - 2, _thumbnailSize - 2);
            return new ThumbnailInfo(thumbnail, ThumbnailState.Loaded);
        }
        catch (Exception)
        {
            // エラーが発生した場合はエラー画像をキャッシュに追加
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
