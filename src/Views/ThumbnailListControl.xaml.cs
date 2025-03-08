using System;
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
using System.Collections.Specialized;
using System.Windows.Threading;
using WpfToolkit.Controls;
using System.Windows.Controls.Primitives;
using Illustra.Controls;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GongSolutions.Wpf.DragDrop;
using System.Collections;
using System.Windows.Documents;
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
        private readonly Queue<Func<Task>> _thumbnailLoadQueue = new Queue<Func<Task>>();
        private readonly DispatcherTimer _thumbnailLoadTimer;
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
                var dataObject = e.Data as DataObject;
                if (e.Data is FileNodeModel || e.Data is IEnumerable<FileNodeModel>)
                {
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
            Loaded += ThumbnailListControl_Loaded;

            // 設定を読み込む
            _appSettings = SettingsHelper.GetSettings();

            // ViewModelの初期化
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            var db = ContainerLocator.Container.Resolve<DatabaseManager>();

            GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(ThumbnailItemsControl, new CustomDropHandler(this));
            GongSolutions.Wpf.DragDrop.DragDrop.SetDragHandler(ThumbnailItemsControl, new DefaultDragHandler());
            GongSolutions.Wpf.DragDrop.DragDrop.SetDragPreviewItemsSorter(ThumbnailItemsControl, new CustomPreviewItemSorter());
            GongSolutions.Wpf.DragDrop.DragDrop.SetDragAdornerTranslation(ThumbnailItemsControl, new Point(5, 20));

            // キーボードイベントハンドラのバインド
            ThumbnailItemsControl.KeyDown += ThumbnailItemsControl_KeyDown;

            // DatabaseManagerの取得とサムネイルローダーの初期化
            _thumbnailLoader = new ThumbnailLoaderHelper(ThumbnailItemsControl, SelectThumbnail, this, _viewModel, db);
            _thumbnailLoader.FileNodesLoaded += OnFileNodesLoaded;

            // ファイルシステム監視の初期化
            _fileSystemMonitor = new FileSystemMonitor(this);

            // サムネイルサイズを設定から復元
            ThumbnailSizeSlider.Value = _appSettings.ThumbnailSize;
            ThumbnailSizeText.Text = _appSettings.ThumbnailSize.ToString();
            _thumbnailLoader.ThumbnailSize = _appSettings.ThumbnailSize;

            // サムネイルロード用のタイマーを初期化
            _thumbnailLoadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _thumbnailLoadTimer.Tick += async (s, e) => await ProcessThumbnailLoadQueue();
            _thumbnailLoadTimer.Start();

            // フィルターボタンの初期状態を設定
            UpdateFilterButtonStates(0);

            // ソート設定を復元
            _isSortByDate = _appSettings.SortByDate;
            _isSortAscending = _appSettings.SortAscending;
            SortTypeText.Text = _isSortByDate ? "日付" : "名前";
            SortDirectionText.Text = _isSortAscending ? "↑" : "↓";

            _isInitialized = true;
        }

        private void ThumbnailListControl_Loaded(object sender, RoutedEventArgs e)
        {
            // ContainerLocatorを使ってEventAggregatorを取得
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視
            _eventAggregator.GetEvent<SelectFileRequestEvent>().Subscribe(OnSelectFileRequest);
            _eventAggregator.GetEvent<RatingChangedEvent>().Subscribe(OnRatingChanged);
        }

        private async void OnFolderSelected(FolderSelectedEventArgs args)
        {
            string folderPath = args.Path;
            if (folderPath == _currentFolderPath)
                return;

            // フォルダが変わったらフィルタを自動的に解除
            _currentRatingFilter = 0;
            UpdateFilterButtonStates(0);
            _viewModel.ApplyRatingFilter(0);

            // ファイルノードをロード（これによりOnFileNodesLoadedが呼ばれる）
            // 以前のフォルダの監視を停止
            if (_fileSystemMonitor.IsMonitoring)
            {
                _fileSystemMonitor.StopMonitoring();
            }

            LoadFileNodes(folderPath);

            // 新しいフォルダの監視を開始
            _fileSystemMonitor.StartMonitoring(folderPath);

            // ソート条件を適用
            await SortThumbnailAsync(_isSortByDate, _isSortAscending);
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
            Dispatcher.InvokeAsync(() =>
            {
                // 選択したアイテムをビューに表示
                if (_viewModel.SelectedItems.Any())
                {
                    ThumbnailItemsControl.ScrollIntoView(_viewModel.SelectedItems.First());
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
            // フィルタリングされたアイテムのリストを取得
            var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
            if (filteredItems.Count <= 1)
                return null;

            // 現在の画像のインデックスを検索
            var currentIndex = filteredItems.FindIndex(i => i.FullPath == currentFilePath);
            if (currentIndex < 0)
                return null;

            // 前のインデックスを計算（リストの最初の場合は最後に循環）
            var prevIndex = (currentIndex > 0) ? currentIndex - 1 : filteredItems.Count - 1;
            return filteredItems[prevIndex].FullPath;
        }

        /// <summary>
        /// 指定されたファイルパスの次の画像ファイルパスを取得します
        /// </summary>
        /// <param name="currentFilePath">現在の画像ファイルパス</param>
        /// <returns>次の画像のファイルパス、存在しない場合はnull</returns>
        public string? GetNextImage(string currentFilePath)
        {
            // フィルタリングされたアイテムのリストを取得
            var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
            if (filteredItems.Count <= 1)
                return null;

            // 現在の画像のインデックスを検索
            var currentIndex = filteredItems.FindIndex(i => i.FullPath == currentFilePath);
            if (currentIndex < 0)
                return null;

            // 次のインデックスを計算（リストの最後の場合は最初に循環）
            var nextIndex = (currentIndex < filteredItems.Count - 1) ? currentIndex + 1 : 0;
            return filteredItems[nextIndex].FullPath;
        }

        /// <summary>
        /// サムネイルのロード完了時に前回選択したファイルを選択する処理
        /// </summary>
        #region IFileSystemChangeHandler Implementation
        public void OnFileCreated(string path)
        {
            Debug.WriteLine($"File created: {path}");
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
                        _viewModel.AddItem(fileNode);

                        // サムネイル生成をトリガー
                        var index = _viewModel.Items.IndexOf(fileNode);
                        if (index >= 0)
                        {
                            await _thumbnailLoader.LoadMoreThumbnailsAsync(index, index);
                        }
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
            Debug.WriteLine($"File renamed: {oldPath} -> {newPath}");
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
            Debug.WriteLine("OnFileNodesLoaded");
            try
            {
                if (_viewModel.Items.Count == 0)
                {
                    return;
                }

                // ソート条件を適用
                _ = SortThumbnailAsync(_isSortByDate, _isSortAscending);

                string? filePath = null;
                bool needFocus = !_isFirstLoaded;
                if (!_isFirstLoaded)
                {
                    // 初回ロード時の処理
                    if (File.Exists(_appSettings.LastSelectedFilePath))
                    {
                        filePath = _appSettings.LastSelectedFilePath;
                    }
                    _isFirstLoaded = true;
                }
                else
                {
                    // 最初のアイテムを選択
                    var firstItem = _viewModel.Items.FirstOrDefault();
                    if (firstItem != null)
                    {
                        filePath = firstItem.FullPath;
                    }
                }

                // 初回起動時のみリストアイテムにフォーカスを設定させる
                if (needFocus)
                {
                    SelectThumbnail(filePath);
                    // 選択中のサムネイルにフォーカスを設定
                    // レイアウト更新後にフォーカスを設定するための処理
                    EventHandler layoutUpdatedHandler = null;
                    layoutUpdatedHandler = (s, e) =>
                    {
                        // イベントは一度だけ処理するため、ハンドラを削除
                        ThumbnailItemsControl.LayoutUpdated -= layoutUpdatedHandler;

                        _ = Dispatcher.InvokeAsync(() =>
                        {
                            var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromItem(ThumbnailItemsControl.SelectedItem) as ListViewItem;
                            container?.Focus();
                        }, DispatcherPriority.Input); // InputはRenderの後に処理される
                    };

                    ThumbnailItemsControl.LayoutUpdated += layoutUpdatedHandler;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サムネイルのロード中にエラーが発生しました: {ex.Message}");
            }
        }


        public async Task SortThumbnailAsync(bool sortByDate, bool sortAscending)
        {
            var currentSelectedPath = _viewModel.SelectedItems.LastOrDefault()?.FullPath;
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

        private void OnSelectFileRequest(string filePath)
        {
            Debug.WriteLine($"OnSelectFileRequest: {filePath}");
            if (_viewModel.Items.Count == 0)
            {
                _eventAggregator?.GetEvent<FileSelectedEvent>()?.Publish("");
                return;
            }

            // アイテムが選択済みの場合は何もしない
            if (_viewModel.SelectedItems.Any())
            {
                return;
            }

            if (filePath == "")
            {
                var item = _viewModel.Items[0];
                if (item == null) return;
                filePath = item.FullPath;
            }
            SelectThumbnail(filePath);

            // サムネイルにフォーカスを設定
            // await Task.Delay(50);
            // ThumbnailItemsControl.Focus();
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

            DisplayGeneratedItemsInfo(ThumbnailItemsControl);

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
                            "選択したファイルは現在のレーティングフィルターで表示されていません。フィルターを解除しますか？",
                            "フィルター解除の確認",
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
            if (e.VerticalChange != 0 || e.HorizontalChange != 0)
            {
                // 表示範囲の前後のサムネイルをロード
                var scrollViewer = e.OriginalSource as ScrollViewer;
                if (scrollViewer != null)
                {
                    await LoadVisibleThumbnailsAsync(scrollViewer);
                }
            }
        }

        private async Task ProcessThumbnailLoadQueue()
        {
            if (_thumbnailLoadQueue.Count > 0)
            {
                var loadTask = _thumbnailLoadQueue.Dequeue();
                await loadTask();
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

        /// <summary>
        /// サムネイルがクリックされたときの処理
        /// </summary>
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

        private void ThumbnailItemsControl_KeyDown(object sender, KeyEventArgs e)
        {
            // ナビゲーションキーの処理
            var selectedIndex = ThumbnailItemsControl.SelectedIndex;
            var panel = UIHelper.FindVisualChild<VirtualizingWrapPanel>(ThumbnailItemsControl);
            if (panel != null)
            {
                var targetItem = HandleNavigationKey(e);
                if (targetItem != null)
                {
                    ThumbnailItemsControl.SelectedItem = targetItem;
                    ThumbnailItemsControl.ScrollIntoView(targetItem);
                }
            }

            // レーティングキーの処理
            if (HandleRatingKey(e))
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// ナビゲーションキー（矢印キー、Home、End、Return）の処理を行います
        /// </summary>
        private FileNodeModel? HandleNavigationKey(KeyEventArgs e)
        {
            FileNodeModel? targetItem = null;

            switch (e.Key)
            {
                case Key.Return:
                    if (_viewModel.SelectedItems.Any())
                    {
                        ShowImageViewer(_viewModel.SelectedItems.Last().FullPath);
                        e.Handled = true;
                        return null;
                    }
                    break;
            }

            return targetItem;
        }


        /// <summary>
        /// レーティングキーの処理を行います
        /// </summary>
        private bool HandleRatingKey(KeyEventArgs e)
        {
            if (e.Key >= Key.D1 && e.Key <= Key.D5)
            {
                SetRating(e.Key - Key.D1 + 1);
                e.Handled = true;
                return true;
            }
            else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad5)
            {
                SetRating(e.Key - Key.NumPad1 + 1);
                e.Handled = true;
                return true;
            }
            else if (e.Key == Key.D0 || e.Key == Key.NumPad0 || e.Key == Key.X)
            {
                SetRating(0);
                e.Handled = true;
                return true;
            }
            else if (e.Key == Key.Z)
            {
                SetRating(5);
                e.Handled = true;
                return true;
            }
            return false;
        }


        // スライダーの値が変更されたときの処理
        private void ThumbnailSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 初期化が完了していない場合は何もしない
            if (!_isInitialized) return;

            // 整数値として取得
            int newSize = (int)e.NewValue;

            // サイズ表示を更新（TextBlockがnullでないことを確認）
            if (ThumbnailSizeText != null)
                ThumbnailSizeText.Text = newSize.ToString();

            // サムネイルローダーにサイズを設定（nullチェック）
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
                    Dispatcher.InvokeAsync(async () =>
                    {
                        await LoadVisibleThumbnailsAsync(scrollViewer);
                    }, DispatcherPriority.Background);
                }
            }
        }

        public async void ThumbnailItemsControl_Drop(IDropInfo e)
        {
            // サムネイル一覧からサムネイル一覧へのドロップ無効
            var dataObject = e.Data as DataObject;
            if (e.Data is FileNodeModel || e.Data is IEnumerable<FileNodeModel>)
            {
                return;
            }

            // look for drag&drop new files
            if (dataObject != null && dataObject.ContainsFileDropList())
            {
                var files = dataObject.GetFileDropList().OfType<string>().ToList();
                if (files == null)
                    return;

                var targetPath = _currentFolderPath;
                if (string.IsNullOrEmpty(targetPath))
                    return;

                // 画像ファイルのみをフィルタリング
                var imageFiles = files.Where(file => FileHelper.IsImageFile(file)).ToList();
                if (!imageFiles.Any())
                    return;

                bool isCopy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
                var operation = isCopy ? DragDropEffects.Copy : DragDropEffects.Move;

                try
                {
                    foreach (var file in imageFiles)
                    {
                        var fileName = Path.GetFileName(file);
                        var destPath = Path.Combine(targetPath, fileName);

                        if (isCopy)
                        {
                            // コピー処理
                            await Task.Run(() => File.Copy(file, destPath, true));
                        }
                        else
                        {
                            // 移動処理（同じドライブ内ならFile.Move、異なるドライブ間ではコピー&削除）
                            if (Path.GetPathRoot(file) == Path.GetPathRoot(destPath))
                            {
                                await Task.Run(() => File.Move(file, destPath, true));
                            }
                            else
                            {
                                await Task.Run(() =>
                                {
                                    File.Copy(file, destPath, true);
                                    File.Delete(file);
                                });
                            }
                        }
                    }

                    e.Effects = operation;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"ファイルの{(isCopy ? "コピー" : "移動")}中にエラーが発生しました: {ex.Message}",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Effects = DragDropEffects.None;
                }
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

        private void OnFileOperationProgress(object? sender, FileOperationProgressEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                // 進捗状況をステータスバーなどに表示
                Debug.WriteLine($"File Operation Progress: {e.CurrentFile}/{e.TotalFiles} - {e.OperationType}");
            });
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
                viewer.Show();
                viewer.Focus(); // ビューアウィンドウにフォーカスを設定
            }
            catch (Exception ex)
            {
                MessageBox.Show($"画像の表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public MainViewModel GetViewModel()
        {
            return _viewModel;
        }

        internal void LoadFileNodes(string path, int rating = 0)
        {
            _currentFolderPath = path;
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

        private void RatingFilter_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) ||
                !int.TryParse(button.Tag?.ToString(), out int rating))
                return;
            ApplyFilterling(rating);
        }
        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilterling(-1);
        }

        private void UpdateFilterButtonStates(int selectedRating)
        {
            // すべてのフィルターボタンをリセット
            foreach (var button in new[] { Filter1, Filter2, Filter3, Filter4, Filter5 })
            {
                if (int.TryParse(button.Tag?.ToString(), out int position))
                {
                    var starControl = UIHelper.FindVisualChild<RatingStarControl>(button);
                    if (starControl != null)
                    {
                        starControl.IsFilled = position <= selectedRating;
                        starControl.StarFill = position <= selectedRating ?
                            RatingHelper.GetRatingColor(position) :
                            Brushes.Transparent;
                        starControl.TextColor = position <= selectedRating ?
                            RatingHelper.GetTextColor(position) :
                            Brushes.Gray;
                    }
                }
            }

            // フィルター解除ボタンの状態を更新
            ClearFilterButton.IsEnabled = selectedRating > 0;
        }

        private void ApplyFilterling(int rating)
        {
            try
            {
                // 現在のフォーカスアイテムを保存
                var focusedItem = _viewModel.SelectedItems.LastOrDefault();
                var focusedPath = focusedItem?.FullPath;

                _currentRatingFilter = rating;
                UpdateFilterButtonStates(rating);

                // レーティングフィルターの適用
                if (rating > 0)
                {
                    _viewModel.ApplyRatingFilter(rating); // 選択されたレーティングのみを表示
                }
                else
                {
                    _viewModel.ApplyRatingFilter(0); // フィルタなし
                }

                // フィルタ後のアイテムリスト
                var filteredItems = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList();

                // 選択するアイテムを決定
                FileNodeModel? itemToSelect = null;
                if (focusedPath != null)
                {
                    // 前回フォーカスされていたアイテムがフィルタ後も存在する場合はそれを選択
                    itemToSelect = filteredItems.FirstOrDefault(fi => fi.FullPath == focusedPath);
                }

                // フォーカスアイテムが見つからない場合は先頭のアイテムを選択
                if (itemToSelect == null && filteredItems.Any())
                {
                    itemToSelect = filteredItems.First();
                }

                // 選択を更新
                _viewModel.SelectedItems.Clear();
                if (itemToSelect != null)
                {
                    _viewModel.SelectedItems.Add(itemToSelect);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ThumbnailItemsControl.ScrollIntoView(itemToSelect);
                        var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromItem(itemToSelect) as ListViewItem;
                        container?.Focus();
                    });
                }

                ClearFilterButton.IsEnabled = rating != 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フィルタークリア中にエラーが発生: {ex.Message}");
            }
        }

        private void OnRatingChanged(RatingChangedEventArgs args)
        {
            var fileNode = _viewModel.Items.FirstOrDefault(fn => fn.FullPath == args.FilePath);
            if (fileNode != null)
            {
                fileNode.Rating = args.Rating;
                ApplyFilterling(_currentRatingFilter); // フィルターを再適用

                // 選択中のファイルのレーティングが変更された場合はアニメーション実行
                foreach (var selectedItem in _viewModel.SelectedItems)
                {
                    if (selectedItem.FullPath == args.FilePath)
                    {
                        // UIスレッドで実行
                        Dispatcher.InvokeAsync(() =>
                        {
                            try
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
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"アニメーション実行中にエラー: {ex.Message}");
                            }
                        }, DispatcherPriority.Background);
                    }
                }
            }
        }

        // DataTemplate内の特定の名前を持つ要素を検索するヘルパーメソッド
        private T? FindElementInTemplate<T>(FrameworkElement container, string elementName) where T : FrameworkElement
        {
            if (container == null)
                return null;

            T? result = null;

            // コンテナ内のすべての子要素を検索
            var childCount = VisualTreeHelper.GetChildrenCount(container);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(container, i) as DependencyObject;
                if (child == null) continue;

                // 目的の型と名前に一致する要素を検索
                if (child is T element && (element.Name == elementName || string.IsNullOrEmpty(elementName)))
                {
                    return element;
                }

                // 再帰的に子要素を検索
                if (child is FrameworkElement frameworkElement)
                {
                    result = FindElementInTemplate<T>(frameworkElement, elementName);
                    if (result != null)
                        return result;
                }
            }

            return result;
        }


        private async void SortToggle_Click(object sender, RoutedEventArgs e)
        {
            _isSortAscending = !_isSortAscending;
            _appSettings.SortAscending = _isSortAscending;
            _thumbnailLoader.SortAscending = _isSortAscending;
            SettingsHelper.SaveSettings(_appSettings);
            SortDirectionText.Text = _isSortAscending ? "↑" : "↓";
            await SortThumbnailAsync(_isSortByDate, _isSortAscending);
        }

        private async void SortTypeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isSortByDate = !_isSortByDate;
            _appSettings.SortByDate = _isSortByDate;
            _thumbnailLoader.SortByDate = _isSortByDate;
            SettingsHelper.SaveSettings(_appSettings);
            SortTypeText.Text = _isSortByDate ? "日付" : "名前";
            await SortThumbnailAsync(_isSortByDate, _isSortAscending);
        }

        /// <summary>
        /// VirtualizingWrapPanelの実際のレイアウトから列数を取得します
        /// </summary>
        private int GetItemsPerRow(VirtualizingWrapPanel panel)
        {
            if (ThumbnailItemsControl.Items.Count == 0 || panel == null)
                return 1;

            // パネルの幅から推定される列数を計算（フォールバック用）
            double expectedItemWidth = ThumbnailSizeSlider.Value + 12; // マージンとパディングを考慮
            int estimatedColumns = Math.Max(1, (int)(panel.ActualWidth / expectedItemWidth));

            var scrollViewer = UIHelper.FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
            if (scrollViewer == null)
                return estimatedColumns;

            // Viewportの範囲を取得
            var viewport = new Rect(new Point(scrollViewer.HorizontalOffset, scrollViewer.VerticalOffset),
                                  new Size(scrollViewer.ViewportWidth, scrollViewer.ViewportHeight));

            double? firstRowY = null;
            int itemsInFirstRow = 0;

            // 表示範囲内の最初の行を見つけてカウント
            for (int i = 0; i < ThumbnailItemsControl.Items.Count; i++)
            {
                var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (container != null)
                {
                    var bounds = container.TransformToAncestor(scrollViewer).TransformBounds(new Rect(container.RenderSize));
                    if (bounds.IntersectsWith(viewport))
                    {
                        var pos = container.TransformToAncestor(panel).Transform(new Point(0, 0));
                        if (!firstRowY.HasValue)
                        {
                            firstRowY = pos.Y;
                            itemsInFirstRow = 1;
                        }
                        else if (Math.Abs(pos.Y - firstRowY.Value) <= 1) // 1ピクセルの誤差を許容
                        {
                            itemsInFirstRow++;
                        }
                        else if (pos.Y > firstRowY.Value)
                        {
                            // 次の行に到達したら終了
                            return itemsInFirstRow;
                        }
                    }
                }
            }

            return itemsInFirstRow > 0 ? itemsInFirstRow : estimatedColumns;
        }
    }
}
