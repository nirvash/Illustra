using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Illustra.Helpers
{
    /// <summary>
    /// サムネイルの読み込みと管理を行うヘルパークラス
    /// </summary>
    public class ThumbnailLoaderHelper
    {
        private List<string> _imageFiles = new List<string>();
        private int _loadedThumbnails = 0;
        private const int ThumbnailBatchSize = 20;
        private ConcurrentDictionary<string, BitmapSource> _thumbnailCache = new ConcurrentDictionary<string, BitmapSource>();
        private CancellationTokenSource? _cancellationTokenSource;
        private string _currentFolderPath = string.Empty;
        private readonly ItemsControl _thumbnailListBox;
        private int _thumbnailSize = 120;
        private readonly SemaphoreSlim _loadingSemaphore = new SemaphoreSlim(4, 4); // 同時に読み込む最大数を4に制限
        private bool _isLoadingBatch = false;
        private readonly object _loadingLock = new object();

        /// <summary>
        /// サムネイルローダーを初期化します
        /// </summary>
        /// <param name="thumbnailListBox">サムネイルを表示するItemsControl</param>
        public ThumbnailLoaderHelper(ItemsControl thumbnailListBox)
        {
            _thumbnailListBox = thumbnailListBox ?? throw new ArgumentNullException(nameof(thumbnailListBox));
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
                    // 現在のフォルダがあれば再ロード
                    if (!string.IsNullOrEmpty(_currentFolderPath))
                    {
                        LoadThumbnails(_currentFolderPath);
                    }
                }
            }
        }

        /// <summary>
        /// 指定されたフォルダのサムネイルを読み込みます
        /// </summary>
        /// <param name="folderPath">サムネイルを読み込むフォルダのパス</param>
        public async void LoadThumbnails(string folderPath)
        {
            _currentFolderPath = folderPath;

            // 前回のロード処理をキャンセル
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            try
            {
                _imageFiles = await Task.Run(() =>
                    Directory.GetFiles(folderPath, "*.*")
                        .Where(file => IsImageFile(file))
                        .ToList(),
                    token);

                _loadedThumbnails = 0;
                lock (_loadingLock)
                {
                    _isLoadingBatch = false;
                }
                Application.Current.Dispatcher.Invoke(() => _thumbnailListBox.Items.Clear());

                // 最初のバッチを読み込む
                await LoadNextBatchAsync(token);
            }
            catch (OperationCanceledException)
            {
                // キャンセルされた場合は何もしない
            }
            catch (Exception ex)
            {
                MessageBox.Show($"サムネイルの読み込み中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 次のバッチのサムネイルを読み込みます
        /// </summary>
        private async Task LoadNextBatchAsync(CancellationToken cancellationToken = default)
        {
            bool shouldLoad;
            lock (_loadingLock)
            {
                shouldLoad = !_isLoadingBatch && _loadedThumbnails < _imageFiles.Count;
                if (shouldLoad)
                {
                    _isLoadingBatch = true;
                }
            }

            if (!shouldLoad) return;

            try
            {
                await LoadMoreThumbnailsAsync(cancellationToken);

                // 次のバッチがあればロード予約を行う
                bool hasMoreImages;
                lock (_loadingLock)
                {
                    hasMoreImages = _loadedThumbnails < _imageFiles.Count;
                    _isLoadingBatch = false;
                }

                if (hasMoreImages && !cancellationToken.IsCancellationRequested)
                {
                    // 少し遅延を入れて次のバッチをロード
                    await Task.Delay(300, cancellationToken);
                    await LoadNextBatchAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                lock (_loadingLock)
                {
                    _isLoadingBatch = false;
                }
            }
            catch (Exception ex)
            {
                // エラーログ記録など
                System.Diagnostics.Debug.WriteLine($"バッチロードエラー: {ex.Message}");
                lock (_loadingLock)
                {
                    _isLoadingBatch = false;
                }

                // エラーが発生しても次バッチのロードを試みる
                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, cancellationToken); // エラー時は長めの遅延
                    await LoadNextBatchAsync(cancellationToken);
                }
            }
        }

        /// <summary>
        /// さらに多くのサムネイルを読み込みます（スクロール時など）
        /// </summary>
        /// <param name="cancellationToken">キャンセルトークン</param>
        public async Task LoadMoreThumbnailsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                List<string> thumbnailsToLoad;
                lock (_loadingLock)
                {
                    thumbnailsToLoad = _imageFiles
                        .Skip(_loadedThumbnails)
                        .Take(ThumbnailBatchSize)
                        .ToList();
                }

                if (!thumbnailsToLoad.Any())
                    return;

                var loadingTasks = new List<Task>();

                foreach (var imageFile in thumbnailsToLoad)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var thumbnailContainer = new Border
                    {
                        Width = _thumbnailSize,
                        Height = _thumbnailSize,
                        Margin = new Thickness(3),
                        BorderThickness = new Thickness(1),
                        BorderBrush = Brushes.LightGray
                    };

                    var thumbnail = new Image
                    {
                        Width = _thumbnailSize - 2,
                        Height = _thumbnailSize - 2,
                        Stretch = Stretch.Uniform,
                        Source = GenerateDummyImage(_thumbnailSize - 2, _thumbnailSize - 2)
                    };

                    thumbnailContainer.Child = thumbnail;
                    Application.Current.Dispatcher.Invoke(() => _thumbnailListBox.Items.Add(thumbnailContainer));

                    // 各画像のロード処理をリストに追加
                    var loadingTask = Task.Run(async () =>
                    {
                        try
                        {
                            // セマフォを使って同時に処理する数を制限
                            await _loadingSemaphore.WaitAsync(cancellationToken);
                            try
                            {
                                // キャッシュをチェックして、なければ新たに生成
                                var thumbnailImage = await GetOrCreateThumbnailAsync(imageFile, cancellationToken);

                                if (!cancellationToken.IsCancellationRequested)
                                {
                                    await Application.Current.Dispatcher.InvokeAsync(() =>
                                    {
                                        thumbnail.Source = thumbnailImage;

                                        // ツールチップに画像のファイル名を表示
                                        thumbnailContainer.ToolTip = Path.GetFileName(imageFile);
                                    });
                                }
                            }
                            finally
                            {
                                _loadingSemaphore.Release();
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // キャンセルされた場合は何もしない
                        }
                        catch (Exception ex)
                        {
                            // エラーが発生した場合はシンプルなエラーアイコンを表示
                            try
                            {
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    thumbnail.Source = GenerateErrorImage(_thumbnailSize - 2, _thumbnailSize - 2);
                                    thumbnailContainer.ToolTip = $"エラー: {ex.Message}";
                                });
                            }
                            catch { /* 最後のエラー表示の例外は無視 */ }
                        }
                    }, cancellationToken);

                    loadingTasks.Add(loadingTask);
                }

                // 読み込み済み画像数を更新
                lock (_loadingLock)
                {
                    _loadedThumbnails += thumbnailsToLoad.Count;
                }

                // 待機する必要はないが、例外が発生した場合のために処理
                await Task.WhenAll(loadingTasks.Select(t => t.ContinueWith(task =>
                {
                    if (task.IsFaulted && task.Exception != null && !(task.Exception.InnerException is OperationCanceledException))
                    {
                        // エラーをログに記録するなど必要な処理
                        System.Diagnostics.Debug.WriteLine($"サムネイル読み込みエラー: {task.Exception.InnerException?.Message}");
                    }
                }, TaskContinuationOptions.NotOnCanceled)));
            }
            catch (OperationCanceledException)
            {
                // キャンセルされた場合は何もしない
            }
        }

        /// <summary>
        /// 与えられたキャンセルトークンを使って、現在のサムネイル読み込み処理をキャンセルします
        /// </summary>
        public CancellationToken GetCurrentCancellationToken()
        {
            return _cancellationTokenSource?.Token ?? CancellationToken.None;
        }

        private async Task<BitmapSource> GetOrCreateThumbnailAsync(string imagePath, CancellationToken cancellationToken)
        {
            // サムネイルサイズが変更された場合は、キャッシュキーにサイズ情報を含める
            string cacheKey = $"{imagePath}_{_thumbnailSize}";

            if (_thumbnailCache.TryGetValue(cacheKey, out var cachedThumbnail))
            {
                return cachedThumbnail;
            }

            var thumbnail = await ThumbnailHelper.CreateThumbnailAsync(imagePath, _thumbnailSize - 2, _thumbnailSize - 2);
            _thumbnailCache[cacheKey] = thumbnail;
            return thumbnail;
        }

        private bool IsImageFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png" ||
                   extension == ".gif" || extension == ".bmp" || extension == ".webp";
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
            renderTargetBitmap.Freeze();
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
            renderTargetBitmap.Freeze();
            return renderTargetBitmap;
        }
    }
}
