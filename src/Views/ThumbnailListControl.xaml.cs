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
using System.Windows.Controls.Primitives;
using Illustra.Controls;

namespace Illustra.Views
{
    public partial class ThumbnailListControl : UserControl, IActiveAware
    {
        private IEventAggregator _eventAggregator = null!;
        private MainViewModel _viewModel;
        // 画像閲覧用
        private ImageViewerWindow? _currentViewerWindow;
        private string? _currentSelectedFilePath;
        private string? _currentFolderPath;

        private AppSettings _appSettings;
        private bool _isFirstLoaded = false;
        private ThumbnailLoaderHelper _thumbnailLoader;

        private bool _isInitialized = false;

        private readonly Queue<Func<Task>> _thumbnailLoadQueue = new Queue<Func<Task>>();
        private readonly DispatcherTimer _thumbnailLoadTimer;
        private const string CONTROL_ID = "ThumbnailList";


        #region IActiveAware Implementation
#pragma warning disable 0067 // 使用されていませんという警告を無視
        public bool IsActive { get; set; }
        public event EventHandler? IsActiveChanged;
#pragma warning restore 0067 // 警告の無視を終了
        #endregion

        // xaml でインスタンス化するためのデフォルトコンストラクタ
        public ThumbnailListControl()
        {
            InitializeComponent();
            Loaded += ThumbnailListControl_Loaded;

            // ViewModelの初期化
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // 設定を読み込む
            _appSettings = SettingsHelper.GetSettings();

            // キーイベントのハンドラを追加
            PreviewKeyDown += ThumbnailListControl_PreviewKeyDown;

            // サムネイルローダーの初期化
            _thumbnailLoader = new ThumbnailLoaderHelper(ThumbnailItemsControl, SelectThumbnail, this, _viewModel);
            _thumbnailLoader.FileNodesLoaded += OnFileNodesLoaded;

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

        private void OnFolderSelected(FolderSelectedEventArgs args)
        {
            if (args.Path == _currentFolderPath)
                return;

            // ファイルノードをロード（これによりOnFileNodesLoadedが呼ばれる）
            LoadFileNodes(args.Path);
        }

        public void SaveAllData()
        {
            // 現在のサムネイルサイズを保存
            _appSettings.ThumbnailSize = (int)ThumbnailSizeSlider.Value;
            _appSettings.LastSelectedFilePath = _currentSelectedFilePath ?? "";
        }

        /// <summary>
        /// マウスホイールでサムネイルをスクロールする処理
        /// </summary>
        private void ThumbnailItemsControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
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
                if (_viewModel.SelectedItem != null)
                {
                    ThumbnailItemsControl.ScrollIntoView(_viewModel.SelectedItem);
                }
            }, DispatcherPriority.Render);
        }
        private async void ThumbnailItemsControl_Loaded(object sender, RoutedEventArgs e)
        {
            var scrollViewer = await Task.Run(() => FindVisualChild<ScrollViewer>(ThumbnailItemsControl));
            if (scrollViewer != null)
            {
                // 実際のScrollViewerにイベントハンドラを直接登録
                scrollViewer.ScrollChanged += OnScrollChanged;
            }

            // キーボードナビゲーションのイベントハンドラを追加
            ThumbnailItemsControl.KeyDown += ThumbnailItemsControl_KeyDown;

            // ListViewの選択状態が変更されたときのイベントハンドラを追加
            ThumbnailItemsControl.SelectionChanged += (s, args) =>
            {
                if (args.AddedItems.Count > 0 && args.AddedItems[0] is FileNodeModel selectedItem)
                {
                    // _viewModel とは TwoWay バインディングされているため、ここでの更新は不要
                    _currentSelectedFilePath = selectedItem.FullPath;

                    // FileSelectedEvent を発行
                    _eventAggregator?.GetEvent<FileSelectedEvent>()?.Publish(selectedItem.FullPath);

                    LoadFilePropertiesAsync(selectedItem.FullPath);
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
        private void OnFileNodesLoaded(object? sender, EventArgs e)
        {
            Debug.WriteLine("OnFileNodesLoaded");
            try
            {
                if (_viewModel.Items.Count == 0)
                {
                    return;
                }

                string? filePath = null;
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
                if (filePath != null)
                {
                    SelectThumbnail(filePath);
                    // ThumbnailItemsControl.Focus(); // 初期フォーカスを取得
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サムネイルのロード中にエラーが発生しました: {ex.Message}");
            }
        }


        public async Task SortThumbnailAsync(bool sortByDate, bool sortAscending)
        {
            _viewModel.SetCurrentFolder(_currentFolderPath ?? "");
            await _viewModel.SortItemsAsync(sortByDate, sortAscending);

            // 再選択してスクロール
            if (!string.IsNullOrEmpty(_currentSelectedFilePath))
            {
                SelectThumbnail(_currentSelectedFilePath);
            }
        }

        private async void OnSelectFileRequest(string filePath)
        {
            Debug.WriteLine($"OnSelectFileRequest: {filePath}");
            if (_viewModel.Items.Count == 0)
            {
                _eventAggregator?.GetEvent<FileSelectedEvent>()?.Publish("");
                return;
            }

            // アイテムが選択済みの場合は何もしない
            if (_viewModel.SelectedItem != null)
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
            await Task.Delay(50);
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
                _currentSelectedFilePath = filePath;
                _viewModel.SelectedItem = matchingItem;
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
                                _currentSelectedFilePath = filePath;
                                _viewModel.SelectedItem = matchingItem;
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

                    if (ThumbnailItemsControl == null || _viewModel.Items.Count == 0)
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
        private void Thumbnail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FileNodeModel fileNode)
            {
                // ViewModelのSelectedItemを更新
                SelectThumbnail(fileNode.FullPath);

                // 親のListViewにフォーカスを与える
                // ThumbnailItemsControl.Focus();
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

        /// <summary>
        /// ウィンドウ全体でのキー入力を処理するハンドラ
        /// </summary>
        private void ThumbnailListControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // タブキーを無効化して外側のコントロールにフォーカス移動
            if (e.Key == Key.Tab)
            {
                e.Handled = true;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    ThumbnailItemsControl.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
                }
                else
                {
                    ThumbnailItemsControl.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                }
                return;
            }

            // ThumbnailItemsControlが有効な場合、キー操作を処理
            if (_viewModel.Items.Count > 0 &&
                (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down ||
                 e.Key == Key.Home || e.Key == Key.End || e.Key == Key.Enter))
            {

                // Enterキーが押されて選択アイテムがある場合はビューアを表示
                if (e.Key == Key.Return && _viewModel.SelectedItem != null)
                {
                    ShowImageViewer(_viewModel.SelectedItem.FullPath);
                    e.Handled = true;
                    return;
                }

                // 直接ThumbnailItemsControl_KeyDownメソッドを呼び出して処理
                ThumbnailItemsControl_KeyDown(ThumbnailItemsControl, e);

                // イベントが処理されたことを示す
                if (e.Handled)
                {
                    return;
                }
            }
        }


        private void ThumbnailItemsControl_KeyDown(object sender, KeyEventArgs e)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
            if (scrollViewer == null) return;

            var panel = FindVisualChild<VirtualizingWrapPanel>(scrollViewer);
            if (panel == null) return;

            var selectedIndex = ThumbnailItemsControl.SelectedIndex;
            if (selectedIndex == -1 && _viewModel.FilteredItems.Cast<FileNodeModel>().Any())
            {
                // 選択がない場合は先頭を選択
                selectedIndex = 0;
            }
            if (selectedIndex == -1) return;

            int itemsPerRow = Math.Max(1, (int)(panel.ActualWidth / (ThumbnailSizeSlider.Value + 6))); // 6はマージン
            int totalItems = _viewModel.FilteredItems.Cast<FileNodeModel>().Count();
            int totalRows = (totalItems + itemsPerRow - 1) / itemsPerRow;
            Debug.WriteLine($"selected: {selectedIndex}, total: {totalItems}, rows: {totalRows}");

            FileNodeModel? targetItem = null;

            switch (e.Key)
            {
                case Key.Return:  // Enterキーの代わりにReturnを使用
                    if (_viewModel.SelectedItem != null)
                    {
                        ShowImageViewer(_viewModel.SelectedItem.FullPath);
                        e.Handled = true;
                        return;
                    }
                    break;

                case Key.Home:
                    // 先頭アイテムに移動
                    targetItem = _viewModel.FilteredItems.Cast<FileNodeModel>().FirstOrDefault();
                    e.Handled = true;
                    break;

                case Key.End:
                    // 最後のアイテムに移動
                    targetItem = _viewModel.FilteredItems.Cast<FileNodeModel>().LastOrDefault();
                    e.Handled = true;
                    break;

                case Key.Right:
                    if (selectedIndex + 1 < totalItems)
                    {
                        targetItem = _viewModel.FilteredItems.Cast<FileNodeModel>().ElementAt(selectedIndex + 1);
                        e.Handled = true;
                    }
                    else if (!e.IsRepeat) // キーリピートでない場合のみ循環
                    {
                        // 最後のアイテムで右キーを押したとき、先頭に循環
                        targetItem = _viewModel.FilteredItems.Cast<FileNodeModel>().FirstOrDefault();
                        e.Handled = true;
                    }
                    break;

                case Key.Left:
                    // 左端かどうかチェック
                    if (selectedIndex % itemsPerRow == 0)
                    {
                        // 左端で左キーを押したとき
                        // 前の行の番号を計算
                        int prevRow = (selectedIndex / itemsPerRow) - 1;

                        // 負の行番号にならないようにチェック（先頭行の場合）
                        if (prevRow >= 0)
                        {
                            // 前の行の右端のインデックスを計算
                            int targetIndex = (prevRow * itemsPerRow) + (itemsPerRow - 1);

                            // 存在するアイテム数を超えないように制限
                            targetIndex = Math.Min(targetIndex, totalItems - 1);
                            targetItem = _viewModel.FilteredItems.Cast<FileNodeModel>().ElementAt(targetIndex);
                            e.Handled = true;
                        }
                        else if (!e.IsRepeat) // キーリピートでない場合のみ循環
                        {
                            // 先頭行の左端の場合は、最終行の右端に移動（循環ナビゲーション）
                            int lastRow = (totalItems - 1) / itemsPerRow;
                            int lastRowItemCount = totalItems - (lastRow * itemsPerRow);
                            int targetIndex = (lastRow * itemsPerRow) + Math.Min(itemsPerRow, lastRowItemCount) - 1;
                            targetItem = _viewModel.FilteredItems.Cast<FileNodeModel>().ElementAt(targetIndex);
                            e.Handled = true;
                        }
                    }
                    else
                    {
                        // 通常の左キー処理
                        if (selectedIndex - 1 >= 0)
                        {
                            targetItem = _viewModel.FilteredItems.Cast<FileNodeModel>().ElementAt(selectedIndex - 1);
                            e.Handled = true;
                        }
                    }
                    break;

                case Key.Up:
                    // 上の行の同じ列の位置を計算
                    if (selectedIndex >= itemsPerRow)
                    {
                        // 通常の上移動
                        int targetIndex = selectedIndex - itemsPerRow;
                        targetItem = _viewModel.FilteredItems.Cast<FileNodeModel>().ElementAt(targetIndex);
                        e.Handled = true;
                    }
                    else if (!e.IsRepeat) // キーリピートでない場合のみ循環
                    {
                        // 最上段から最下段へ循環
                        int currentColumn = selectedIndex % itemsPerRow;
                        int lastRowStartIndex = ((totalItems - 1) / itemsPerRow) * itemsPerRow;
                        int targetIndex = Math.Min(lastRowStartIndex + currentColumn, totalItems - 1);
                        targetItem = _viewModel.FilteredItems.Cast<FileNodeModel>().ElementAt(targetIndex);
                        e.Handled = true;
                    }
                    break;

                case Key.Down:
                    // 下の行の同じ列の位置を計算
                    int nextRowIndex = selectedIndex + itemsPerRow;
                    if (nextRowIndex < totalItems)
                    {
                        // 通常の下移動
                        targetItem = _viewModel.FilteredItems.Cast<FileNodeModel>().ElementAt(nextRowIndex);
                        e.Handled = true;
                    }
                    else if (!e.IsRepeat) // キーリピートでない場合のみ循環
                    {
                        // 最下段から最上段へ循環
                        int currentColumn = selectedIndex % itemsPerRow;
                        int targetIndex = Math.Min(currentColumn, totalItems - 1);
                        targetItem = _viewModel.FilteredItems.Cast<FileNodeModel>().ElementAt(targetIndex);
                        e.Handled = true;
                    }
                    break;
            }

            if (e.Handled && targetItem != null)
            {
                e.Handled = true; // イベントを確実に処理済みとしてマーク

                var index = _viewModel.FilteredItems.Cast<FileNodeModel>().ToList().IndexOf(targetItem);
                Debug.WriteLine($"target: {index}, path: {targetItem.FullPath}");

                // ViewModelを通じて選択を更新
                _viewModel.SelectedItem = targetItem;
                _currentSelectedFilePath = targetItem.FullPath;
                // TBD: ファイルを開いたイベント発火
                // LoadFilePropertiesAsync(targetItem.FullPath);

                // スクロール処理とフォーカス処理
                ThumbnailItemsControl.ScrollIntoView(targetItem);
            }
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
                var scrollViewer = FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
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

        // ヘルパーメソッド: 子要素を検索
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            // Check if we're on the UI thread
            if (!parent.Dispatcher.CheckAccess())
            {
                // If not, invoke the method on the UI thread and wait for the result
                return parent.Dispatcher.Invoke(() => FindVisualChild<T>(parent));
            }

            // Now safely on UI thread
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T t)
                {
                    return t;
                }
                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
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
                    var starControl = FindVisualChild<RatingStarControl>(button);
                    if (starControl != null)
                    {
                        starControl.IsFilled = position <= selectedRating;
                        starControl.StarFill = position <= selectedRating ?
                            RatingHelper.GetRatingColor(position) :
                            Brushes.Transparent;
                        starControl.TextColor = RatingHelper.GetTextColor(position);
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
                if (_viewModel.SelectedItem != null && _viewModel.SelectedItem.FullPath == args.FilePath)
                {
                    // UIスレッドで実行
                    Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            // 選択中アイテムのコンテナを取得
                            var container = ThumbnailItemsControl.ItemContainerGenerator.ContainerFromItem(_viewModel.SelectedItem) as ListViewItem;
                            if (container != null)
                            {
                                // DataTemplateの中のRatingStarControlを検索
                                var starControl = FindVisualChild<RatingStarControl>(container);
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

        // レーティング表示の色を取得するメソッド
        public static Brush GetRatingDisplayColor(int rating)
        {
            return rating switch
            {
                1 => Brushes.Red,
                2 => Brushes.Orange,
                3 => Brushes.Gold,
                4 => Brushes.LightGreen,
                5 => Brushes.DeepSkyBlue,
                _ => Brushes.Gray
            };
        }

    }
}
