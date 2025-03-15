using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Illustra.Helpers;
using System.IO;
using Illustra.Events;
using Illustra.Models;
using Illustra.ViewModels;
using System.Diagnostics;
using System.Windows.Threading;
using WpfToolkit.Controls;
using Illustra.Controls;
using System.ComponentModel;
using GongSolutions.Wpf.DragDrop;
using System.Collections;
using Illustra.Functions;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace Illustra.Views
{
    public partial class ThumbnailListControl : UserControl, IActiveAware, IFileSystemChangeHandler
    {
        private IEventAggregator _eventAggregator = null!;
        private MainViewModel _viewModel;
        // 画像閲覧用
        private ImageViewerWindow? _currentViewerWindow;
        private string? _currentFolderPath;

        private AppSettings _appSettings;
        private bool _isFirstLoaded = false;
        private ThumbnailLoaderHelper _thumbnailLoader;
        private FileSystemMonitor _fileSystemMonitor;

        private bool _isInitialized = false;
        private bool _isUpdatingSelection = false;  // 選択状態の更新中フラグ
        private bool _isDragging = false;
        private readonly DispatcherTimer _resizeTimer;

        // 新しいImageViewModelの追加
        private ImageViewModel _imageViewModel;

        // クラスレベルの変数を追加
        private bool _isFirstLoad = true;
        private bool _pendingSelection = false;
        private int _pendingSelectionIndex = -1;

        private bool _isPromptFilterEnabled = false;
        private List<string> _currentTagFilters = new List<string>();
        private bool _isTagFilterEnabled = false;

        /// <summary>
        /// ViewModelの選択状態をUIに反映します
        /// </summary>
        private void UpdateUISelection()
        {
            if (_isUpdatingSelection) return;
            _isUpdatingSelection = true;

            try
            {
                ThumbnailItemsControl.SelectedItems.Clear();
                foreach (var item in _viewModel.SelectedItems)
                {
                    ThumbnailItemsControl.SelectedItems.Add(item);
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }

        private const string CONTROL_ID = "ThumbnailList";
        private bool _isSortAscending = true;
        private bool _isSortByDate = true;

        #region IActiveAware Implementation
#pragma warning disable 0067 // 使用されていませんという警告を無視
        public bool IsActive { get; set; }
        public event EventHandler? IsActiveChanged;
#pragma warning restore 0067 // 警告の無視を終了
        #endregion

        public class CustomDropHandler : DefaultDropHandler
        {
            private ThumbnailListControl _control = null;
            public CustomDropHandler(ThumbnailListControl control)
            {
                _control = control;
            }
            public override void DragOver(IDropInfo e)
            {
                base.DragOver(e);
                // サムネイル一覧からサムネイル一覧へのドロップ無効

                // カスタムデータフォーマットを確認
                var dataObject = e.Data as IDataObject;
                if (dataObject != null && dataObject.GetDataPresent(typeof(FileNodeModel).Name))
                {
                    e.DropTargetAdorner = null;
                    return;
                }

                if (e.DropTargetAdorner == DropTargetAdorners.Insert)
                {
                    e.DropTargetAdorner = null;
                }

                // look for drag&drop new files
                if (dataObject != null && dataObject.GetDataPresent(DataFormats.FileDrop))
                {
                    bool isCopy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
                    e.Effects = isCopy ? DragDropEffects.Copy : DragDropEffects.Move;
                }
            }

            public override void Drop(IDropInfo e)
            {
                _control.ThumbnailItemsControl_Drop(e);
            }
        }

        private class CustomPreviewItemSorter : IDragPreviewItemsSorter
        {
            public IEnumerable SortDragPreviewItems(IEnumerable items)
            {
                var itemList = items.Cast<object>().ToList(); // リストに変換
                var count = Math.Min(itemList.Count, 4); // 最大4つ、でも要素数が4未満ならその数に調整
                var allItems = itemList.GetRange(0, count); // 安全な範囲で取得
                var compositeItem = new CompositeItem { Items = allItems };
                return new[] { compositeItem };
            }

            // コンテナクラス
            public class CompositeItem
            {
                public IEnumerable<object> Items { get; set; }
            }
        }

        // xaml でインスタンス化するためのデフォルトコンストラクタ
        public ThumbnailListControl()
        {
            InitializeComponent();

            // 新しいImageViewModelの初期化
            _imageViewModel = new ImageViewModel();

            // 既存のコード
            _resizeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _resizeTimer.Tick += async (s, e) =>
            {
                _resizeTimer.Stop();
                await UpdateThumbnailSize();
            };

            // 既存のコード
            var dropHandler = new CustomDropHandler(this);
            var dragPreviewItemsSorter = new CustomPreviewItemSorter();
            DragDrop.SetDropHandler(ThumbnailItemsControl, dropHandler);
            DragDrop.SetDragPreviewItemsSorter(ThumbnailItemsControl, dragPreviewItemsSorter);

            // 既存のコード
            ThumbnailItemsControl.PreviewMouseWheel += ThumbnailItemsControl_PreviewMouseWheel;
            ThumbnailItemsControl.PreviewKeyDown += ThumbnailItemsControl_PreviewKeyDown;
            ThumbnailItemsControl.MouseDoubleClick += Thumbnail_MouseDoubleClick;
        }

        private void ThumbnailListControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized) return;
            _isInitialized = true;

            // 既存のコード
            _eventAggregator = ServiceLocator.Current.GetInstance<IEventAggregator>();
            _viewModel = DataContext as MainViewModel ?? new MainViewModel();
            _appSettings = ServiceLocator.Current.GetInstance<AppSettings>();
            _thumbnailLoader = ServiceLocator.Current.GetInstance<ThumbnailLoaderHelper>();
            _fileSystemMonitor = ServiceLocator.Current.GetInstance<FileSystemMonitor>();

            // 既存のコード
            _fileSystemMonitor.RegisterHandler(this);
            _viewModel.FileNodesLoaded += OnFileNodesLoaded;

            // イベント購読
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected);
            _eventAggregator.GetEvent<ShortcutKeyEvent>().Subscribe(OnShortcutKeyReceived);
            _eventAggregator.GetEvent<RatingChangedEvent>().Subscribe(OnRatingChanged);
            _eventAggregator.GetEvent<LanguageChangedEvent>().Subscribe(args => OnLanguageChanged());

            // 新しいImageViewModelを使用するためのバインディング設定
            ThumbnailItemsControl.ItemsSource = _imageViewModel.DisplayItems;

            // 選択変更イベントの設定
            ThumbnailItemsControl.SelectionChanged += (s, args) =>
            {
                if (ThumbnailItemsControl.SelectedItem is FileNodeModel selectedItem)
                {
                    _imageViewModel.SelectedItem = selectedItem;
                }
            };

            // 初期設定の適用
            ApplySettings();
        }

        private void OnShortcutKeyReceived(ShortcutKeyEventArgs args)
        {
            // 自分自身から発行されたイベントは無視
            if (args.SourceId == CONTROL_ID)
                return;

            // Ctrl+C (コピー)
            if (args.Key == Key.C && args.Modifiers == ModifierKeys.Control)
            {
                CopySelectedImagesToClipboard();
            }
            // Ctrl+V (貼り付け)
            else if (args.Key == Key.V && args.Modifiers == ModifierKeys.Control)
            {
                PasteFilesFromClipboard();
            }
            // Ctrl+X (切り取り)
            else if (args.Key == Key.X && args.Modifiers == ModifierKeys.Control)
            {
                // 切り取り処理を実装（必要に応じて）
                // 現在は未実装
            }
            // Ctrl+A (すべて選択)
            else if (args.Key == Key.A && args.Modifiers == ModifierKeys.Control)
            {
                ThumbnailItemsControl.SelectAll();
            }
            // Delete (削除)
            else if (args.Key == Key.Delete && args.Modifiers == ModifierKeys.None)
            {
                DeleteSelectedItems();
            }
        }

        private async void OnFilterChanged(FilterChangedEventArgs args)
        {
            // 新しいImageViewModelを使用したフィルタリング
            _currentRatingFilter = args.RatingFilter;
            _isPromptFilterEnabled = args.IsPromptFilterEnabled;
            _currentTagFilters = args.TagFilters;
            _isTagFilterEnabled = args.IsTagFilterEnabled;

            UpdateFilterButtonStates(_currentRatingFilter);

            await _imageViewModel.ApplyFilterAsync(_currentRatingFilter);
        }

        private async void OnFolderSelected(FolderSelectedEventArgs args)
        {
            _currentFolderPath = args.FolderPath;

            // 新しいImageViewModelを使用したフォルダ読み込み
            await _imageViewModel.LoadImagesFromFolderAsync(args.FolderPath);

            // 初期選択アイテムの設定
            if (!string.IsNullOrEmpty(args.InitialSelectedFilePath))
            {
                SelectThumbnail(args.InitialSelectedFilePath);
            }
            else if (_imageViewModel.DisplayItems.Count > 0)
            {
                ThumbnailItemsControl.SelectedItem = _imageViewModel.DisplayItems[0];
            }
        }

        public void SaveAllData()
        {
            // 現在のサムネイルサイズを保存
            _appSettings.ThumbnailSize = (int)ThumbnailSizeSlider.Value;
            _appSettings.LastSelectedFilePath = _viewModel.SelectedItems.LastOrDefault()?.FullPath ?? "";

            // ソート条件を保存
            _appSettings.SortByDate = _isSortByDate;
            _appSettings.SortAscending = _isSortAscending;

            // 設定を保存
            SettingsHelper.SaveSettings(_appSettings);
        }

        /// <summary>
        /// マウスホイールでサムネイルをスクロールする処理
        /// </summary>
        private void ThumbnailItemsControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
                if (scrollViewer != null)
                {
                    double multiplier = _appSettings?.MouseWheelMultiplier ?? 1.0;
                    double scrollAmount = (e.Delta * multiplier) / 3;

                    // 現在のオフセット位置から計算した新しい位置にスクロール
                    double newOffset = scrollViewer.VerticalOffset - scrollAmount;
                    scrollViewer.ScrollToVerticalOffset(newOffset);

                    // スクロールイベントが処理されたことをマーク
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"マウスホイールスクロール処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// サムネイル一覧の選択を指定されたファイルパスに同期します
        /// </summary>
        /// <param name="filePath">選択するファイルパス</param>
        public void SyncThumbnailSelection(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            // サムネイルの選択と表示を更新
            SelectThumbnail(filePath);

            // UIスレッドで実行することを確保
            Dispatcher.InvokeAsync(async () =>
            {
                // 選択したアイテムをビューに表示
                if (_viewModel.SelectedItems.Any())
                {
                    var selectedItem = _viewModel.SelectedItems.First();
                    ThumbnailItemsControl.ScrollIntoView(selectedItem);

                    // レイアウトの更新を待機
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                    // コンテナを取得して画面内に表示
                    var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromItem(selectedItem) as ListViewItem;
                    container?.BringIntoView();
                }
            }, DispatcherPriority.Render);
        }

        private async void ThumbnailItemsControl_Loaded(object sender, RoutedEventArgs e)
        {
            var scrollViewer = await Task.Run(() => UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl));
            if (scrollViewer != null)
            {
                // 実際のScrollViewerにイベントハンドラを直接登録
                scrollViewer.ScrollChanged += OnScrollChanged;

                // ウィンドウのサイズ変更イベントを購読
                var window = Window.GetWindow(this);
                if (window != null)
                {
                    window.SizeChanged += async (s, args) => await OnWindowSizeChanged(scrollViewer);
                }
            }

            // プロパティ変更通知の購読
            ((INotifyPropertyChanged)_viewModel).PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.SelectedItems))
                {
                    UpdateUISelection();
                }
            };

            // ListViewの選択状態が変更されたときのイベントハンドラを追加
            ThumbnailItemsControl.SelectionChanged += (s, args) =>
            {
                if (_isUpdatingSelection) return;
                _isUpdatingSelection = true;

                try
                {
                    // 削除された項目を処理
                    foreach (FileNodeModel item in args.RemovedItems)
                    {
                        _viewModel.RemoveSelectedItemSilently(item);
                    }

                    // 選択アイテムの更新（ViewModelのCollectionChangedイベントでIsLastSelectedが更新される）
                    foreach (FileNodeModel item in args.AddedItems)
                    {
                        _viewModel.AddSelectedItemSilently(item);
                    }

                    // 削除された項目を処理
                    foreach (FileNodeModel item in args.RemovedItems)
                    {
                        _viewModel.RemoveSelectedItemSilently(item);
                    }

                    // イベントの発行（最後に選択されたアイテムがある場合のみ）
                    var lastSelected = _viewModel.SelectedItems.LastOrDefault();
                    if (lastSelected != null)
                    {
                        _eventAggregator?.GetEvent<FileSelectedEvent>()?.Publish(lastSelected.FullPath);
                        LoadFilePropertiesAsync(lastSelected.FullPath);
                    }
                }
                finally
                {
                    _isUpdatingSelection = false;
                }
            };
        }

        private void LoadFilePropertiesAsync(string fullPath)
        {
            // TBD ファイルのプロパティをロードして表示する処理
            // ファイル選択イベントの発行によってこの処理は不要になる可能性がある
        }

        /// <summary>
        /// 指定されたファイルパスの前の画像ファイルパスを取得します
        /// </summary>
        /// <param name="currentFilePath">現在の画像ファイルパス</param>
        /// <returns>前の画像のファイルパス、存在しない場合はnull</returns>
        public string? GetPreviousImage(string currentFilePath)
        {
            bool enableCyclicNavigation = IsCyclicNavigationEnabled();

            // フィルタリングされたアイテムのリストを取得
            var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
            if (filteredItems.Count <= 1)
                return null;

            // 現在の画像のインデックスを検索
            var currentIndex = filteredItems.FindIndex(i => i.FullPath == currentFilePath);
            if (currentIndex < 0)
                return null;

            // 前のインデックスを計算
            if (currentIndex > 0)
            {
                // 通常の移動
                return filteredItems[currentIndex - 1].FullPath;
            }
            else if (IsCyclicNavigationEnabled())
            {
                // 循環移動が有効な場合は最後に移動
                return filteredItems[filteredItems.Count - 1].FullPath;
            }
            return null;
        }

        /// <summary>
        /// 指定されたファイルパスの次の画像ファイルパスを取得します
        /// </summary>
        /// <param name="currentFilePath">現在の画像ファイルパス</param>
        /// <returns>次の画像のファイルパス、存在しない場合はnull</returns>
        public string? GetNextImage(string currentFilePath)
        {
            bool enableCyclicNavigation = IsCyclicNavigationEnabled();

            // フィルタリングされたアイテムのリストを取得
            var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
            if (filteredItems.Count <= 1)
                return null;

            // 現在の画像のインデックスを検索
            var currentIndex = filteredItems.FindIndex(i => i.FullPath == currentFilePath);
            if (currentIndex < 0)
                return null;

            // 次のインデックスを計算
            if (currentIndex < filteredItems.Count - 1)
            {
                // 通常の移動
                return filteredItems[currentIndex + 1].FullPath;
            }
            else if (IsCyclicNavigationEnabled())
            {
                // 循環移動が有効な場合は先頭に移動
                return filteredItems[0].FullPath;
            }
            return null;
        }

        /// <summary>
        /// サムネイルのロード完了時に前回選択したファイルを選択する処理
        /// </summary>
        #region IFileSystemChangeHandler Implementation
        public void OnFileCreated(string path)
        {
            Debug.WriteLine($"[サムネイル] ファイル作成: {path}");
            if (!FileHelper.IsImageFile(path)) return;

            Dispatcher.Invoke(async () =>
            {
                try
                {
                    // 既存のファイルノードをチェック
                    if (_viewModel.Items.Any(x => x.FullPath == path))
                    {
                        Debug.WriteLine($"File already exists in the list: {path}");
                        return;
                    }

                    var fileNode = await _thumbnailLoader.CreateFileNodeAsync(path);
                    if (fileNode != null)
                    {
                        // ソート順に従って適切な位置に挿入
                        _viewModel.AddItem(fileNode);

                        _ = Task.Run(async () =>
                        {
                            await _viewModel.UpdatePromptCacheAsync(path);

                            // RefreshFiltering を await で完了を待ってから次の処理に進む
                            await Dispatcher.InvokeAsync(() =>
                            {
                                _viewModel.RefreshFiltering();
                            }).Task;  // Task を取得して await

                            // サムネイル生成をトリガー
                            var index = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList().IndexOf(fileNode);
                            if (index >= 0)
                            {
                                await _thumbnailLoader.LoadMoreThumbnailsAsync(index, index);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing created file: {ex.Message}");
                }
            });
        }

        public void OnFileDeleted(string path)
        {
            Dispatcher.Invoke(() =>
            {
                var fileNode = _viewModel.Items.FirstOrDefault(x => x.FullPath == path);
                if (fileNode != null)
                {
                    _viewModel.Items.Remove(fileNode);
                }
            });
        }

        public void OnFileRenamed(string oldPath, string newPath)
        {
            Debug.WriteLine($"[サムネイル] ファイル名変更: {oldPath} -> {newPath}");
            if (!FileHelper.IsImageFile(newPath)) return;

            Dispatcher.Invoke(async () =>
            {
                try
                {
                    // 一覧から検索
                    var existingNode = _viewModel.Items.FirstOrDefault(x => x.FullPath == oldPath);

                    // 新規ノードの取得
                    var newNode = await _thumbnailLoader.HandleFileRenamed(oldPath, newPath);
                    if (existingNode != null)
                    {
                        if (newNode != null)
                        {
                            existingNode.FullPath = newNode.FullPath;
                            existingNode.Rating = newNode.Rating;
                            existingNode.FileName = newNode.FileName;
                            existingNode.ThumbnailInfo = newNode.ThumbnailInfo;

                            // サムネイル生成をトリガー
                            var index = _viewModel.Items.IndexOf(existingNode);
                            if (index >= 0)
                            {
                                await _thumbnailLoader.LoadMoreThumbnailsAsync(index, index);
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"Error processing renamed file: {newPath}. {oldPath} is not found.");
                        }
                    }
                    else if (newNode != null)
                    {
                        OnFileCreated(newPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing renamed file: {ex.Message}");
                }
            });
        }
        #endregion

        private void OnFileNodesLoaded(object? sender, EventArgs e)
        {
            try
            {
                if (_viewModel.Items.Count == 0)
                {
                    return;
                }

                // ソート条件を適用
                _ = SortThumbnailAsync(_isSortByDate, _isSortAscending, false);
                _pendingSelection = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サムネイルのロード中にエラーが発生しました: {ex.Message}");
            }
        }


        public async Task SortThumbnailAsync(bool sortByDate, bool sortAscending, bool selectItem = false)
        {
            var currentSelectedPath = selectItem ? _viewModel.SelectedItems.LastOrDefault()?.FullPath : null;
            _viewModel.SetCurrentFolder(_currentFolderPath ?? "");
            await _viewModel.SortItemsAsync(sortByDate, sortAscending);

            if (!string.IsNullOrEmpty(currentSelectedPath))
            {
                // UIの更新を待つ
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                // 選択とスクロールを実行
                SelectThumbnail(currentSelectedPath);

                // ScrollIntoViewを確実に実行
                var item = _viewModel.FilteredItems.Cast<FileNodeModel>().FirstOrDefault(x => x.FullPath == currentSelectedPath);
                if (item != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ThumbnailItemsControl.ScrollIntoView(item);
                        ThumbnailItemsControl.UpdateLayout();
                    }, DispatcherPriority.Render);

                    // 現在表示されている範囲のサムネイルを再ロード
                    var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
                    if (scrollViewer != null)
                    {
                        await LoadVisibleThumbnailsAsync(scrollViewer);
                    }
                }
            }
        }


        /// <summary>
        /// 指定されたファイルを選択します
        /// </summary>
        private void SelectThumbnail(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine("Invalid file path or file does not exist");
                return;
            }

            // DisplayGeneratedItemsInfo(ThumbnailItemsControl);

            // まずフィルター適用後のアイテムリストから検索
            var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>();
            var matchingItem = filteredItems.FirstOrDefault(x => x.FullPath == filePath);

            if (matchingItem != null)
            {
                if (!ThumbnailItemsControl.SelectedItems.Contains(matchingItem))
                {
                    ThumbnailItemsControl.SelectedItems.Add(matchingItem);
                }
                _viewModel.SelectedItems.Clear();
                _viewModel.SelectedItems.Add(matchingItem);
                ThumbnailItemsControl.ScrollIntoView(matchingItem);

                // FileSelectedEvent を発行
                _eventAggregator?.GetEvent<FileSelectedEvent>()?.Publish(filePath);

                // LastSelectedFilePath を保存
                _appSettings.LastSelectedFilePath = filePath;
                SettingsHelper.SaveSettings(_appSettings);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No matching item found in filtered list");

                // フィルターされていない元のItemsから検索
                matchingItem = _viewModel.Items.FirstOrDefault(x => x.FullPath == filePath);
                if (matchingItem != null)
                {
                    System.Diagnostics.Debug.WriteLine("Item found in original list but filtered out");

                    // フィルター解除が必要
                    if (_currentRatingFilter > 0)
                    {
                        // ユーザーにフィルターが適用されていて見つからない旨を通知
                        MessageBoxResult result = MessageBox.Show(
                            (string)Application.Current.FindResource("String_Thumbnail_RatingFilterMessage"),
                            (string)Application.Current.FindResource("String_Thumbnail_RatingFilterTitle"),
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            // フィルターを解除
                            ApplyFilterling(0);

                            // 再度検索
                            matchingItem = _viewModel.FilteredItems.Cast<FileNodeModel>().FirstOrDefault(x => x.FullPath == filePath);
                            if (matchingItem != null)
                            {
                                if (!ThumbnailItemsControl.SelectedItems.Contains(matchingItem))
                                {
                                    ThumbnailItemsControl.SelectedItems.Add(matchingItem);
                                }
                                _viewModel.SelectedItems.Clear();
                                _viewModel.SelectedItems.Add(matchingItem);
                                ThumbnailItemsControl.ScrollIntoView(matchingItem);

                                // FileSelectedEvent を発行
                                _eventAggregator?.GetEvent<FileSelectedEvent>()?.Publish(filePath);

                                // LastSelectedFilePath を保存
                                _appSettings.LastSelectedFilePath = filePath;
                                SettingsHelper.SaveSettings(_appSettings);
                            }
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("No matching item found even in the original list");
                    }
                }
            }
        }

        private async void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                // 新しいImageViewModelを使用したサムネイル読み込み
                var visibleItems = GetVisibleItems(scrollViewer);
                if (visibleItems.Any())
                {
                    _imageViewModel.EnqueueThumbnailLoad(visibleItems, true);
                }
            }
        }

        private IEnumerable<FileNodeModel> GetVisibleItems(ScrollViewer scrollViewer)
        {
            var panel = FindVisualChild<VirtualizingWrapPanel>(scrollViewer);
            if (panel == null) return Enumerable.Empty<FileNodeModel>();

            var visibleItems = new List<FileNodeModel>();
            var itemsPerRow = GetItemsPerRow(panel);
            if (itemsPerRow <= 0) return Enumerable.Empty<FileNodeModel>();

            var verticalOffset = scrollViewer.VerticalOffset;
            var viewportHeight = scrollViewer.ViewportHeight;
            var itemHeight = panel.ItemHeight;

            var startRow = (int)(verticalOffset / itemHeight);
            var endRow = (int)((verticalOffset + viewportHeight) / itemHeight) + 1;

            var startIndex = startRow * itemsPerRow;
            var endIndex = Math.Min(endRow * itemsPerRow, _imageViewModel.DisplayItems.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                if (i >= 0 && i < _imageViewModel.DisplayItems.Count)
                {
                    visibleItems.Add(_imageViewModel.DisplayItems[i]);
                }
            }

            return visibleItems;
        }

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }
                else
                {
                    var result = FindVisualChild<T>(child);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            return null;
        }

        private async Task OnWindowSizeChanged(ScrollViewer scrollViewer)
        {
            // 新しいImageViewModelを使用したサムネイル読み込み
            var visibleItems = GetVisibleItems(scrollViewer);
            if (visibleItems.Any())
            {
                _imageViewModel.EnqueueThumbnailLoad(visibleItems, true);
            }
        }

        private async void ThumbnailItemsControl_StatusChanged(object sender, EventArgs e)
        {
            if (!_pendingSelection)
                return;

            if (ThumbnailItemsControl.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
            {
                _pendingSelection = false;  // 早めにフラグを解除して重複実行を防ぐ

                await Task.Run(async () =>
                {
                    // UIスレッドでの遅延実行
                    await Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        // レイアウト更新を待機
                        await Task.Delay(100);
                        try
                        {
                            string? filePath = null;
                            bool needFocus = !_isFirstLoaded;

                            // 優先順位：
                            // 1. 指定された初期選択ファイル
                            // 2. 初回起動時の最後に選択されていたファイル
                            // 3. リストの最初のアイテム
                            if (_pendingInitialSelectedFilePath != null)
                            {
                                filePath = _pendingInitialSelectedFilePath;
                                _pendingInitialSelectedFilePath = null;
                            }
                            else if (!_isFirstLoaded)
                            {
                                // 初回ロード時の処理
                                if (File.Exists(_appSettings.LastSelectedFilePath))
                                {
                                    filePath = _appSettings.LastSelectedFilePath;
                                }
                                _isFirstLoaded = true;
                            }

                            // ファイルパスが無効な場合は最初のアイテムを選択
                            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                            {
                                var firstItem = _viewModel.Items.FirstOrDefault();
                                if (firstItem != null)
                                {
                                    filePath = firstItem.FullPath;
                                }
                            }

                            if (!string.IsNullOrEmpty(filePath))
                            {
                                // レイアウト更新後に選択処理を実行
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    SelectThumbnail(filePath);
                                }, DispatcherPriority.Loaded);

                                // さらにレイアウト更新を待ってフォーカスを設定
                                if (needFocus)
                                {
                                    await Dispatcher.InvokeAsync(() =>
                                    {
                                        var selectedItem = _viewModel.SelectedItems.LastOrDefault();
                                        if (selectedItem != null)
                                        {
                                            var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromItem(selectedItem) as ListViewItem;
                                            container?.Focus();
                                        }
                                    }, DispatcherPriority.Input);
                                }
                            }

                            _isFirstLoad = false;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error in ThumbnailItemsControl_StatusChanged: {ex.Message}");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                });
            }
        }

        private void Thumbnail_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FileNodeModel fileNode)
            {
                ShowImageViewer(fileNode.FullPath);
                e.Handled = true;
            }
        }


        internal void ApplySettings()
        {
            // 設定を再読み込み
            _appSettings = SettingsHelper.GetSettings();

            // サムネイルサイズを設定
            ThumbnailSizeSlider.Value = _appSettings.ThumbnailSize;
        }


        // レーティングを設定する新しいメソッド
        private async void SetRating(int rating)
        {
            if (!_viewModel.SelectedItems.Any()) return;

            var dbManager = ContainerLocator.Container.Resolve<DatabaseManager>();
            var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();

            // W/A 操作中にレーティングフィルタ適用によって SelectedItems が変更されるのでコピー
            var items = _viewModel.SelectedItems.Cast<FileNodeModel>().ToList();
            foreach (var selectedItem in items)
            {
                // 同じレーティングの場合はスキップ
                if (selectedItem.Rating == rating && rating != 0)
                {
                    continue;
                }

                // レーティングを更新
                selectedItem.Rating = rating;

                // データベースを更新
                await dbManager.UpdateRatingAsync(selectedItem.FullPath, rating);

                // イベントを発行して他の画面に通知 (複数まとめて通知させたほうがよさそう)
                eventAggregator?.GetEvent<RatingChangedEvent>()?.Publish(
                    new RatingChangedEventArgs { FilePath = selectedItem.FullPath, Rating = rating });
            }
        }

        private async void PasteFilesFromClipboard()
        {
            if (Clipboard.ContainsFileDropList())
            {
                var dataObject = Clipboard.GetDataObject();
                var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                if (files.Any())
                {
                    // カット操作かどうかを判定
                    bool isCut = false;

                    // より信頼性の高いカット操作の判定方法
                    if (dataObject.GetDataPresent(DataFormats.FileDrop) &&
                        dataObject.GetDataPresent("Preferred DropEffect"))
                    {
                        var memoryStream = dataObject.GetData("Preferred DropEffect") as MemoryStream;
                        if (memoryStream != null)
                        {
                            byte[] bytes = new byte[4];
                            memoryStream.Position = 0;
                            memoryStream.Read(bytes, 0, bytes.Length);
                            // DragDropEffects.Move (2) の場合はカット操作
                            isCut = BitConverter.ToInt32(bytes, 0) == 2;
                        }
                    }

                    // ファイルを処理（isCut=trueの場合は移動、falseの場合はコピー）
                    var processedFiles = await ProcessImageFiles(files, !isCut);

                    if (processedFiles.Any())
                    {
                        if (isCut)
                        {
                            // カット操作の場合はクリップボードをクリア
                            Clipboard.Clear();
                            ShowNotification((string)Application.Current.FindResource("String_Thumbnail_FilesMoved"));
                        }
                        else
                        {
                            ShowNotification((string)Application.Current.FindResource("String_Thumbnail_FilesCopied"));
                        }

                        // ペーストされたファイルの最初のファイルを選択
                        try
                        {
                            if (files.Count > 0)
                            {
                                string firstFile = files[0];
                                string fileName = Path.GetFileName(firstFile);
                                string destPath = Path.Combine(_currentFolderPath ?? "", fileName);

                                // ファイルリストが更新されるのを少し待つ
                                await Task.Delay(100);

                                // ファイルを選択
                                var fileNode = _viewModel.Items.FirstOrDefault(f => f.FullPath == destPath);
                                if (fileNode != null)
                                {
                                    SelectThumbnail(destPath);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ペーストされたファイルの選択中にエラーが発生しました: {ex.Message}");
                        }
                    }
                }
            }
        }

        private void ShowNotification(string message, int fontSize = 24)
        {
            NotificationText.Text = message;
            NotificationText.FontSize = fontSize;
            var storyboard = (Storyboard)FindResource("ShowNotificationStoryboard");
            storyboard.Begin(Notification);
        }

        private void CopySelectedImagesToClipboard()
        {
            if (_viewModel.SelectedItems.Any())
            {
                var imagePaths = _viewModel.SelectedItems.Select(item => item.FullPath).ToArray();
                var dataObject = new DataObject();
                dataObject.SetData(DataFormats.FileDrop, imagePaths);

                // 単独の画像が選択されている場合は、画像形式でもコピー
                if (_viewModel.SelectedItems.Count == 1)
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(imagePaths[0]);
                        bitmap.EndInit();
                        dataObject.SetImage(bitmap);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"画像形式でのコピー中にエラーが発生しました: {ex.Message}");
                    }
                }

                Clipboard.SetDataObject(dataObject, true);

                // リソースから文言を取得して通知を表示
                ShowNotification((string)Application.Current.FindResource("String_Thumbnail_ImageCopied"));
            }
        }

        private void ThumbnailItemsControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var shortcutHandler = KeyboardShortcutHandler.Instance;

            // 修飾キーの場合はデフォルト動作を許可
            if (shortcutHandler.IsModifierKey(e.Key))
            {
                e.Handled = false;
                return;
            }

            // 全選択のショートカットの場合は、ListViewのSelectAllメソッドを呼び出す
            if (shortcutHandler.IsShortcutMatch(FuncId.SelectAll, e.Key))
            {
                ThumbnailItemsControl.SelectAll();
                e.Handled = true;
                return;
            }

            // 画像コピーのショートカットの場合は、画像をクリップボードにコピーする
            if (shortcutHandler.IsShortcutMatch(FuncId.Copy, e.Key))
            {
                CopySelectedImagesToClipboard();
                e.Handled = true;
                return;
            }

            // 画像ペーストのショートカットの場合は、クリップボードから画像をペーストする
            if (shortcutHandler.IsShortcutMatch(FuncId.Paste, e.Key))
            {
                PasteFilesFromClipboard();
                e.Handled = true;
                return;
            }

            // その他のキーの場合は、ListViewのデフォルト動作を無効化
            e.Handled = true;

            // リストの先頭に移動
            if (shortcutHandler.IsShortcutMatch(FuncId.MoveToStart, e.Key))
            {
                if (ThumbnailItemsControl.Items.Count > 0)
                {
                    ThumbnailItemsControl.SelectedIndex = 0;
                    ThumbnailItemsControl.ScrollIntoView(ThumbnailItemsControl.SelectedItem);
                }
                e.Handled = true;
                return;
            }

            // リストの末尾に移動
            if (shortcutHandler.IsShortcutMatch(FuncId.MoveToEnd, e.Key))
            {
                if (ThumbnailItemsControl.Items.Count > 0)
                {
                    ThumbnailItemsControl.SelectedIndex = ThumbnailItemsControl.Items.Count - 1;
                    ThumbnailItemsControl.ScrollIntoView(ThumbnailItemsControl.SelectedItem);
                }
                e.Handled = true;
                return;
            }

            var selectedIndex = ThumbnailItemsControl.SelectedIndex;
            var panel = UIHelper.FindVisualChild<VirtualizingWrapPanel>(ThumbnailItemsControl);
            if (panel != null)
            {
                var targetItem = HandleNavigationKey(e);
                if (targetItem != null)
                {
                    ThumbnailItemsControl.SelectedItem = targetItem;
                    ThumbnailItemsControl.ScrollIntoView(targetItem);
                    return;
                }
            }

            // レーティングキーの処理
            HandleRatingKey(e);
        }

        /// <summary>
        /// ナビゲーションキー（矢印キー、Home、End、Return）の処理を行います
        /// </summary>
        private FileNodeModel? HandleNavigationKey(KeyEventArgs e)
        {
            FileNodeModel? targetItem = null;

            if (KeyboardShortcutHandler.Instance.IsShortcutMatch(FuncId.ToggleViewer, e.Key))
            {
                if (_viewModel.SelectedItems.Any())
                {
                    ShowImageViewer(_viewModel.SelectedItems.Last().FullPath);
                    e.Handled = true;
                    return null;
                }
            }

            if (KeyboardShortcutHandler.Instance.IsShortcutMatch(FuncId.Delete, e.Key))
            {
                if (_viewModel.SelectedItems.Any())
                {
                    DeleteSelectedItems();
                    e.Handled = true;
                    return null;
                }
            }

            var shortcutHandler = KeyboardShortcutHandler.Instance;

            // 循環移動の設定を取得
            var mainWindow = Window.GetWindow(this) as MainWindow;
            bool enableCyclicNavigation = mainWindow?.EnableCyclicNavigation ?? false;

            // 方向キーの判定を行う
            bool isLeft = shortcutHandler.IsShortcutMatch(FuncId.NavigateLeft, e.Key);
            bool isRight = shortcutHandler.IsShortcutMatch(FuncId.NavigateRight, e.Key);
            bool isUp = shortcutHandler.IsShortcutMatch(FuncId.NavigateUp, e.Key);
            bool isDown = shortcutHandler.IsShortcutMatch(FuncId.NavigateDown, e.Key);

            // いずれかの方向キーが押された場合
            if (isLeft || isRight || isUp || isDown)
            {
                e.Handled = true;

                var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
                if (!filteredItems.Any())
                    return null;

                var currentIndex = -1;
                var selectedItem = _viewModel.SelectedItems.LastOrDefault();
                if (selectedItem != null)
                {
                    currentIndex = filteredItems.IndexOf(selectedItem);
                }
                if (currentIndex < 0)
                    return null;

                var panel = UIHelper.FindVisualChild<VirtualizingWrapPanel>(ThumbnailItemsControl);
                if (panel == null)
                    return null;

                var itemsPerRow = GetItemsPerRow(panel);
                if (itemsPerRow <= 0)
                    return null;

                int targetIndex;
                if (isLeft || isRight)
                {
                    targetIndex = GetHorizontalNavigationIndex(currentIndex, isRight, filteredItems.Count);
                }
                else
                {
                    targetIndex = GetVerticalNavigationIndex(currentIndex, isDown, itemsPerRow, filteredItems.Count);
                }

                if (targetIndex >= 0 && targetIndex < filteredItems.Count)
                {
                    targetItem = filteredItems[targetIndex];
                }
            }

            return targetItem;
        }

        private async void DeleteSelectedItems()
        {
            try
            {
                var selectedItems = _viewModel.SelectedItems.ToList();
                if (!selectedItems.Any()) return;

                // 複数選択時は確認ダイアログを表示
                if (selectedItems.Count > 1)
                {
                    var message = string.Format(
                        (string)Application.Current.FindResource("String_Thumbnail_DeleteConfirmMessage"),
                        selectedItems.Count);
                    var result = MessageBox.Show(
                        message,
                        (string)Application.Current.FindResource("String_Thumbnail_DeleteConfirmTitle"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes) return;
                }

                var db = ContainerLocator.Container.Resolve<DatabaseManager>();
                var fileOp = new FileOperationHelper(db);

                foreach (var item in selectedItems)
                {
                    if (File.Exists(item.FullPath))
                    {
                        await fileOp.DeleteFile(item.FullPath);
                        _viewModel.Items.Remove(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ファイルの削除中にエラーが発生しました: {ex.Message}");
            }
        }


        /// <summary>
        /// レーティングキーの処理を行います
        /// </summary>
        private bool HandleRatingKey(KeyEventArgs e)
        {
            for (int i = 1; i <= 5; i++)
            {
                // レーティング設定
                if (KeyboardShortcutHandler.Instance.IsShortcutMatch(FuncId.Ratings[i], e.Key))
                {
                    SetRating(i);
                    e.Handled = true;
                    return true;
                }

                // レーティングフィルター
                var filterId = new FuncId($"filter_rating_{i}");
                if (KeyboardShortcutHandler.Instance.IsShortcutMatch(filterId, e.Key))
                {
                    ApplyFilterling(i);
                    e.Handled = true;
                    return true;
                }
            }

            // レーティング解除
            if (KeyboardShortcutHandler.Instance.IsShortcutMatch(FuncId.Rating0, e.Key))
            {
                SetRating(0);
                e.Handled = true;
                return true;
            }

            // レーティング5の代替キー
            if (KeyboardShortcutHandler.Instance.IsShortcutMatch(FuncId.Rating5, e.Key))
            {
                SetRating(5);
                e.Handled = true;
                return true;
            }

            // フィルター解除
            if (KeyboardShortcutHandler.Instance.IsShortcutMatch(FuncId.FilterRating0, e.Key))
            {
                ApplyFilterling(0);
                e.Handled = true;
                return true;
            }

            return false;
        }


        // スライダーの値が変更されたときの処理（表示の更新のみ）
        /// <summary>
        /// 選択中のサムネイルを画面内に表示します
        /// </summary>
        private async Task EnsureSelectedThumbnailVisibleAsync()
        {
            var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
            if (scrollViewer != null && _viewModel.SelectedItems.Any())
            {
                var selectedItem = _viewModel.SelectedItems.First();

                // レイアウトの更新を待機（複数回待機して確実に完了を待つ）
                for (int i = 0; i < 3; i++)
                {
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                }

                // 選択中のアイテムを画面内に表示
                ThumbnailItemsControl.ScrollIntoView(selectedItem);

                // レイアウトの更新を待機（複数回待機して確実に完了を待つ）
                for (int i = 0; i < 3; i++)
                {
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                }

                // コンテナを取得して画面内に表示
                var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromItem(selectedItem) as ListViewItem;
                container?.BringIntoView();

                // 最終的なレイアウトの更新を待機
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            }
        }

        private void ThumbnailSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                // 初期化が完了していない場合は何もしない
                if (!_isInitialized) return;

                // 整数値として取得
                int newSize = (int)e.NewValue;

                // サイズ表示を更新（TextBlockがnullでないことを確認）
                if (ThumbnailSizeText != null)
                    ThumbnailSizeText.Text = newSize.ToString();

                // ドラッグ中でない場合（クリックでの値変更）はリサイズタイマーを開始
                if (!_isDragging)
                {
                    // 実行中のタイマーがあれば停止し、新しいタイマーを開始
                    // 最後の値変更から300ms後に実行される
                    _resizeTimer.Stop();
                    _resizeTimer.Start();
                }

                // 選択中のサムネイルを画面内に表示
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await EnsureSelectedThumbnailVisibleAsync();
                }, DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サムネイルサイズ変更中にエラーが発生しました: {ex.Message}");
            }
        }

        // スライダーのドラッグが完了したときの処理（サムネイルの再生成）
        private async void ThumbnailSizeSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (!_isInitialized) return;

            _isDragging = false;
            await UpdateThumbnailSize();
        }

        private async Task UpdateThumbnailSize()
        {
            int newSize = (int)ThumbnailSizeSlider.Value;

            // サムネイルローダーにサイズを設定
            if (_thumbnailLoader != null)
            {
                _thumbnailLoader.ThumbnailSize = newSize;

                // サムネイル画面の再描画をリクエスト
                ThumbnailItemsControl.InvalidateMeasure();
                ThumbnailItemsControl.InvalidateVisual();

                // ScrollViewerも更新
                var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
                if (scrollViewer != null)
                {
                    scrollViewer.InvalidateMeasure();
                    scrollViewer.InvalidateVisual();

                    // 現在表示されているサムネイルを再ロード
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await EnsureSelectedThumbnailVisibleAsync();
                        await LoadVisibleThumbnailsAsync(scrollViewer);
                    }, DispatcherPriority.Background);
                }
            }
        }

        /// <summary>
        /// 画像ファイルを指定されたフォルダにコピーまたは移動します
        /// </summary>
        private async Task<List<string>> ProcessImageFiles(List<string> files, bool isCopy)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentFolderPath))
                    return new List<string>();

                var db = ContainerLocator.Container.Resolve<DatabaseManager>();
                var fileOp = new FileOperationHelper(db);

                // フォルダパスを取得
                string targetFolder = _currentFolderPath ?? "";
                var processedFiles = await fileOp.ExecuteFileOperation(files, targetFolder, isCopy);

                foreach (var path in processedFiles)
                {
                    if (!FileHelper.IsImageFile(path)) continue;

                    // 既存のファイルノードをチェック
                    if (_viewModel.Items.Any(x => x.FullPath == path))
                    {
                        Debug.WriteLine($"File already exists in the list: {path}");
                        continue;
                    }

                    var fileNode = await _thumbnailLoader.CreateFileNodeAsync(path);
                    if (fileNode != null)
                    {
                        // ソート順に従って適切な位置に挿入
                        _viewModel.AddItem(fileNode);

                        _ = Task.Run(async () =>
                        {
                            await _viewModel.UpdatePromptCacheAsync(path);

                            // RefreshFiltering を await で完了を待ってから次の処理に進む
                            await Dispatcher.InvokeAsync(() =>
                            {
                                _viewModel.RefreshFiltering();
                            }).Task;

                            // サムネイル生成をトリガー
                            var index = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList().IndexOf(fileNode);
                            if (index >= 0)
                            {
                                await _thumbnailLoader.LoadMoreThumbnailsAsync(index, index);
                            }
                        });
                    }
                }

                // ペーストされたファイルの最初のファイルを選択
                try
                {
                    if (processedFiles.Count > 0)
                    {
                        string firstFile = processedFiles[0];
                        string fileName = Path.GetFileName(firstFile);
                        string destPath = Path.Combine(_currentFolderPath ?? "", fileName);

                        // ファイルリストが更新されるのを少し待つ
                        await Task.Delay(100);

                        // ファイルを選択
                        var fileNode = _viewModel.Items.FirstOrDefault(f => f.FullPath == destPath);
                        if (fileNode != null)
                        {
                            SelectThumbnail(destPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ペーストされたファイルの選択中にエラーが発生しました: {ex.Message}");
                }

                return processedFiles;
            }
            catch (Exception ex)
            {
                string operation = isCopy ?
                    (string)Application.Current.FindResource("String_Thumbnail_FileOperation_Copy") :
                    (string)Application.Current.FindResource("String_Thumbnail_FileOperation_Move");

                MessageBox.Show(
                    string.Format((string)Application.Current.FindResource("String_Thumbnail_FileOperationError"),
                    operation, ex.Message),
                    (string)Application.Current.FindResource("String_Thumbnail_FileOperationErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return new List<string>();
            }
        }

        public async void ThumbnailItemsControl_Drop(IDropInfo e)
        {
            // サムネイル一覧からサムネイル一覧へのドロップ無効
            var dataObject = e.Data as DataObject;
            if (dataObject != null && dataObject.GetDataPresent(typeof(FileNodeModel).Name))
            {
                return;
            }

            // look for drag&drop new files
            if (dataObject != null && dataObject.ContainsFileDropList())
            {
                var files = dataObject.GetFileDropList().OfType<string>().ToList();
                if (files == null)
                    return;

                bool isCopy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
                var processedFiles = await ProcessImageFiles(files, isCopy);
                e.Effects = processedFiles.Any() ? (isCopy ? DragDropEffects.Copy : DragDropEffects.Move) : DragDropEffects.None;
            }
        }

        /// <summary>
        /// ドラッグ＆ドロップ操作の結果を処理します
        /// </summary>
        private async void ProcessDragDropResult(DragDropEffects result, List<FileNodeModel> draggedItems)
        {
            if (result == DragDropEffects.Move)
            {
                // 移動操作の場合、非同期でファイルの存在を確認
                await Task.Run(() =>
                {
                    var removedItems = new List<FileNodeModel>();

                    foreach (var item in draggedItems)
                    {
                        // ファイルが存在しない場合は削除リストに追加
                        if (!File.Exists(item.FullPath))
                        {
                            removedItems.Add(item);
                        }
                    }

                    // UIスレッドで一覧から削除
                    if (removedItems.Any())
                    {
                        Dispatcher.Invoke(() =>
                        {
                            foreach (var item in removedItems)
                            {
                                _viewModel.Items.Remove(item);
                            }

                            // 削除されたファイル数を通知（デバッグ用）
                            Debug.WriteLine($"{removedItems.Count}個のファイルが移動により一覧から削除されました");
                        });
                    }
                });
            }
        }

        private bool IsCyclicNavigationEnabled()
        {
            return App.Instance.EnableCyclicNavigation;
        }


        private void ShowImageViewer(string filePath)
        {
            try
            {
                var viewer = new ImageViewerWindow(filePath)
                {
                    Parent = this
                };
                _currentViewerWindow = viewer; // 現在開いているビューアを追跡

                // 初期状態のフルスクリーンモードを設定
                _thumbnailLoader.SetFullscreenMode(viewer.IsFullScreen);

                // ビューワが全画面表示モードを開始/終了した時のイベントを設定
                viewer.IsFullscreenChanged += (s, e) =>
                {
                    _thumbnailLoader.SetFullscreenMode(viewer.IsFullScreen);
                };

                // ビューワが閉じられた時のイベントを設定
                viewer.Closed += (s, e) =>
                {
                    _thumbnailLoader.SetFullscreenMode(false);
                    _currentViewerWindow = null;

                    // サムネイルの再生成
                    var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
                    if (scrollViewer != null)
                    {
                        _ = LoadVisibleThumbnailsAsync(scrollViewer);
                    }
                };

                viewer.Show();
                viewer.Focus(); // ビューアウィンドウにフォーカスを設定
            }
            catch (Exception ex)
            {
                var message = string.Format(
                    (string)Application.Current.FindResource("String_Thumbnail_ImageDisplayError"),
                    ex.Message);
                MessageBox.Show(message,
                    (string)Application.Current.FindResource("String_Thumbnail_FileOperationErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public MainViewModel GetViewModel()
        {
            return _viewModel;
        }

        /// <summary>
        /// 左右キー入力時の移動先インデックスを取得します
        /// </summary>
        private int GetHorizontalNavigationIndex(int currentIndex, bool isRight, int itemCount)
        {
            bool enableCyclicNavigation = IsCyclicNavigationEnabled();

            if (isRight)
            {
                if (currentIndex < itemCount - 1)
                {
                    // 右移動：次のインデックス
                    return currentIndex + 1;
                }
                else if (enableCyclicNavigation)
                {
                    // 循環移動が有効な場合のみ先頭へ
                    return 0;
                }
                return currentIndex;
            }
            else
            {
                if (currentIndex > 0)
                {
                    // 左移動：前のインデックス
                    return currentIndex - 1;
                }
                else if (enableCyclicNavigation)
                {
                    // 循環移動が有効な場合のみ最後へ
                    return itemCount - 1;
                }
                return currentIndex;
            }
        }

        /// <summary>
        /// 上下キー入力時の移動先インデックスを取得します
        /// </summary>
        private int GetVerticalNavigationIndex(int currentIndex, bool isDown, int itemsPerRow, int itemCount)
        {
            bool enableCyclicNavigation = IsCyclicNavigationEnabled();

            int currentRow = currentIndex / itemsPerRow;
            int currentCol = currentIndex % itemsPerRow;
            int totalRows = (itemCount + itemsPerRow - 1) / itemsPerRow;

            if (isDown)
            {
                // 下移動
                int targetIndex = currentIndex + itemsPerRow;
                if (targetIndex >= itemCount)
                {
                    if (currentRow < totalRows - 1)
                    {
                        // 最後の行に到達した場合、その行の最後のアイテムまでに制限
                        return Math.Min(targetIndex, itemCount - 1);
                    }
                    else if (enableCyclicNavigation)
                    {
                        // 循環移動が有効な場合は最初の行の同じ列へ
                        return currentCol;
                    }
                    return currentIndex;
                }
                return targetIndex;
            }
            else
            {
                // 上移動
                if (currentRow > 0)
                {
                    // 上の行の同じ列へ
                    return currentIndex - itemsPerRow;
                }
                else if (enableCyclicNavigation)
                {
                    // 循環移動が有効な場合は最後の行の同じ列へ（存在する場合のみ）
                    int lastRowIndex = (totalRows - 1) * itemsPerRow + currentCol;
                    return Math.Min(lastRowIndex, itemCount - 1);
                }
                return currentIndex;
            }
        }

        /// <summary>
        /// 選択中のサムネイルにフォーカスを設定します。
        /// </summary>
        public void FocusSelectedThumbnail()
        {
            if (_viewModel.SelectedItems.LastOrDefault() is FileNodeModel selectedItem)
            {
                var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromItem(selectedItem) as ListViewItem;
                Dispatcher.InvokeAsync(() =>
                {
                    if (Window.GetWindow(this)?.IsActive == true)
                    {
                        container?.Focus();
                    }
                }, DispatcherPriority.Input);
            }
        }

        private string? _pendingInitialSelectedFilePath;

        internal void LoadFileNodes(string path, string? initialSelectedFilePath = null)
        {
            _currentFolderPath = path;
            _pendingInitialSelectedFilePath = initialSelectedFilePath;
            _thumbnailLoader.LoadFileNodes(path);
        }



        public void DisplayGeneratedItemsInfo(ListView listView)
        {
            int totalItems = listView.Items.Count;
            int generatedItems = GetGeneratedItemsCount(listView);

            Debug.WriteLine($"全アイテム数: {totalItems}");
            Debug.WriteLine($"生成されたアイテム数: {generatedItems}");
            Debug.WriteLine($"仮想化率: {(1 - (double)generatedItems / totalItems) * 100:F2}%");
        }

        /// <summary>
        /// Gets the number of items that have been generated (realized) by the virtualization system
        /// </summary>
        private int GetGeneratedItemsCount(ListView listView)
        {
            int count = 0;

            if (listView == null)
                return 0;

            for (int i = 0; i < listView.Items.Count; i++)
            {
                var container = listView.ItemContainerGenerator.ContainerFromIndex(i);
                if (container != null)
                {
                    count++;
                }
            }

            return count;
        }
        private int _currentRatingFilter = 0;

        private async void RatingFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string ratingStr && int.TryParse(ratingStr, out int rating))
            {
                // 新しいImageViewModelを使用したフィルタリング
                _currentRatingFilter = rating;
                UpdateFilterButtonStates(rating);
                await _imageViewModel.ApplyFilterAsync(rating);
            }
        }

        private async void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            // 新しいImageViewModelを使用したフィルタクリア
            _currentRatingFilter = 0;
            UpdateFilterButtonStates(0);
            await _imageViewModel.ApplyFilterAsync(0);
        }

        private void UpdateFilterButtonStates(int selectedRating)
        {
            // 既存のコード
            Filter1.Background = selectedRating >= 1 ? new SolidColorBrush(Colors.LightGray) : Brushes.Transparent;
            Filter2.Background = selectedRating >= 2 ? new SolidColorBrush(Colors.LightGray) : Brushes.Transparent;
            Filter3.Background = selectedRating >= 3 ? new SolidColorBrush(Colors.LightGray) : Brushes.Transparent;
            Filter4.Background = selectedRating >= 4 ? new SolidColorBrush(Colors.LightGray) : Brushes.Transparent;
            Filter5.Background = selectedRating >= 5 ? new SolidColorBrush(Colors.LightGray) : Brushes.Transparent;
            ClearFilterButton.IsEnabled = selectedRating > 0;
        }

        private async void OnSortOrderChanged(SortOrderChangedEventArgs args)
        {
            // 新しいImageViewModelを使用したソート
            _isSortByDate = args.SortByDate;
            _isSortAscending = args.SortAscending;

            // UIの更新
            SortDirectionText.Text = _isSortAscending
                ? (string)FindResource("String_Thumbnail_SortAscending")
                : (string)FindResource("String_Thumbnail_SortDescending");

            SortTypeText.Text = _isSortByDate
                ? (string)FindResource("String_Thumbnail_SortByDate")
                : (string)FindResource("String_Thumbnail_SortByName");

            await _imageViewModel.ApplySortAsync(_isSortByDate, _isSortAscending);
        }

        private async void SortToggle_Click(object sender, RoutedEventArgs e)
        {
            // 新しいImageViewModelを使用したソート方向の切り替え
            _isSortAscending = !_isSortAscending;

            // UIの更新
            SortDirectionText.Text = _isSortAscending
                ? (string)FindResource("String_Thumbnail_SortAscending")
                : (string)FindResource("String_Thumbnail_SortDescending");

            await _imageViewModel.ApplySortAsync(_isSortByDate, _isSortAscending);
        }

        private async void SortTypeToggle_Click(object sender, RoutedEventArgs e)
        {
            // 新しいImageViewModelを使用したソート種類の切り替え
            _isSortByDate = !_isSortByDate;

            // UIの更新
            SortTypeText.Text = _isSortByDate
                ? (string)FindResource("String_Thumbnail_SortByDate")
                : (string)FindResource("String_Thumbnail_SortByName");

            await _imageViewModel.ApplySortAsync(_isSortByDate, _isSortAscending);
        }

        private async void OnRatingChanged(RatingChangedEventArgs args)
        {
            var fileNode = _viewModel.Items.FirstOrDefault(fn => fn.FullPath == args.FilePath);
            if (fileNode != null)
            {
                fileNode.Rating = args.Rating;
                // フィルタが設定されている場合のみフィルタを再適用
                if (_currentRatingFilter > 0)
                {
                    await ApplyFilterling(_currentRatingFilter);
                    return;
                }

                // 選択中のファイルのレーティングが変更された場合はアニメーション実行
                foreach (var selectedItem in _viewModel.SelectedItems)
                {
                    if (selectedItem.FullPath == args.FilePath)
                    {
                        // UIスレッドで実行
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                // ウィンドウがアクティブな場合のみフォーカス処理を実行
                                if (Window.GetWindow(this)?.IsActive == true)
                                {
                                    // 選択中アイテムのコンテナを取得
                                    var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromItem(selectedItem) as ListViewItem;
                                    if (container != null)
                                    {
                                        // DataTemplateの中のRatingStarControlを検索
                                        var starControl = UIHelper.FindVisualChild<RatingStarControl>(container);
                                        if (starControl != null)
                                        {
                                            // 明示的にアニメーションを実行
                                            starControl.PlayAnimation();
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"アニメーション実行中にエラー: {ex.Message}");
                            }
                        }, DispatcherPriority.Background);
                    }
                }
            }
        }

        private async Task LoadVisibleThumbnailsAsync(ScrollViewer scrollViewer)
        {
            _thumbnailLoadQueue.Clear();

            _thumbnailLoadQueue.Enqueue(async () =>
            {
                try
                {
                    // ItemsControl が初期化されるのを待つ
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

                    if (ThumbnailItemsControl == null || ThumbnailItemsControl.Items.Count == 0)
                        return;

                    int firstIndexToLoad = 0;
                    int lastIndexToLoad = 0;
                    for (int i = 0; i < ThumbnailItemsControl.Items.Count; i++)
                    {
                        var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                        if (container != null && container.IsVisible)
                        {
                            if (firstIndexToLoad == 0)
                            {
                                firstIndexToLoad = i;
                            }
                            if (i > lastIndexToLoad)
                            {
                                lastIndexToLoad = i;
                            }
                        }
                    }

                    // 前後10個ずつのサムネイルをロード
                    int bufferSize = 10;
                    firstIndexToLoad = Math.Max(0, firstIndexToLoad - bufferSize);
                    lastIndexToLoad = Math.Min(ThumbnailItemsControl.Items.Count - 1, lastIndexToLoad + bufferSize);

                    // 可視範囲のサムネイルをロード
                    await _thumbnailLoader.LoadMoreThumbnailsAsync(firstIndexToLoad, lastIndexToLoad);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LoadVisibleThumbnailsAsync エラー: {ex.Message}");
                }
            });

            await ProcessThumbnailLoadQueue();
        }

        private async Task ProcessThumbnailLoadQueue()
        {
            if (_thumbnailLoadQueue.Count > 0)
            {
                var loadTask = _thumbnailLoadQueue.Dequeue();
                await loadTask();
            }
        }

        private async void OnLanguageChanged()
        {
            // 言語リソースの反映をまつ
            Task.Run(() =>
            {
                // フィルターボタンのテキストを更新
                Dispatcher.Invoke(() =>
                {
                    // ソート種類の文言を更新
                    SortTypeText.Text = _isSortByDate ?
                        (string)Application.Current.FindResource("String_Thumbnail_SortByDate") :
                        (string)Application.Current.FindResource("String_Thumbnail_SortByName");
                    SortDirectionText.Text = _isSortAscending ?
                        (string)Application.Current.FindResource("String_Thumbnail_SortAscending") :
                        (string)Application.Current.FindResource("String_Thumbnail_SortDescending");
                });
            });
        }
    }
}
