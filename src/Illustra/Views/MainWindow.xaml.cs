using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.ViewModels;

namespace Illustra.Views
{
    public partial class MainWindow : Window
    {
        private ThumbnailLoaderHelper _thumbnailLoader;
        private bool _isInitialized = false;
        private AppSettings _appSettings;
        private string _currentSelectedFilePath = string.Empty;
        private MainViewModel _viewModel;
        private bool _isFirstLoaded = false;
        private bool _shouldSelectFirstItem = false;
        private DateTime _lastLoadTime = DateTime.MinValue;
        private readonly object _loadLock = new object();

        // 画像閲覧用
        private ImageViewerWindow? _currentViewerWindow;

        public MainWindow()
        {
            InitializeComponent();

            // 設定を読み込む
            _appSettings = SettingsHelper.GetSettings();

            // ウィンドウサイズと位置を設定から復元
            Width = _appSettings.WindowWidth;
            Height = _appSettings.WindowHeight;
            Left = _appSettings.WindowLeft;
            Top = _appSettings.WindowTop;
            WindowState = _appSettings.WindowState;

            // ViewModelの初期化
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // サムネイルローダーの初期化
            _thumbnailLoader = new ThumbnailLoaderHelper(ThumbnailItemsControl, SelectThumbnail, _viewModel.Items);
            _thumbnailLoader.fileNodesLoaded += OnFileNodesLoaded;

            // サムネイルサイズを設定から復元
            ThumbnailSizeSlider.Value = _appSettings.ThumbnailSize;
            ThumbnailSizeText.Text = _appSettings.ThumbnailSize.ToString();
            _thumbnailLoader.ThumbnailSize = _appSettings.ThumbnailSize;

            _isInitialized = true;

            // フォルダツリーの読み込み
            LoadDrivesAsync();

            // ウィンドウが閉じられるときに設定を保存
            Closing += MainWindow_Closing;

            // ウィンドウがロードされた後に前回のフォルダを選択
            Loaded += MainWindow_Loaded;

            // ウィンドウ全体でキーイベントをキャッチするように設定
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            // プロパティ領域を初期化
            ClearPropertiesDisplay();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 前回開いていたフォルダがある場合、少し時間をおいてから処理開始
            if (!string.IsNullOrEmpty(_appSettings.LastFolderPath) && System.IO.Directory.Exists(_appSettings.LastFolderPath))
            {
                // UIが完全にロードされてから処理を行う
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        // ツリービューが完全に構築されるのを待つ
                        await Task.Delay(500);

                        // ツリービューで前回のフォルダを展開して選択
                        bool selected = await FileSystemHelper.SelectPathInTreeViewAsync(
                            FolderTreeView, _appSettings.LastFolderPath);

                        // 選択に成功しなかった場合は処理完了
                        if (!selected) return;

                        // フォルダの選択状態を設定
                        await Task.Delay(100);
                        if (FolderTreeView.SelectedItem is TreeViewItem selectedItem &&
                            selectedItem.Tag is string path &&
                            path == _appSettings.LastFolderPath)
                        {
                            selectedItem.IsSelected = true;
                            selectedItem.BringIntoView();

                            // フォルダ選択後、サムネイルリストにフォーカスを移動
                            // サムネイルのロード完了を待つ
                            await Task.Delay(300);

                            if (_viewModel.Items.Count > 0)
                            {
                                ThumbnailItemsControl.Focus();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"前回のフォルダを開く際にエラーが発生: {ex}");
                    }
                }));
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 現在のウィンドウ状態を保存
            if (WindowState == WindowState.Normal)
            {
                _appSettings.WindowWidth = Width;
                _appSettings.WindowHeight = Height;
                _appSettings.WindowLeft = Left;
                _appSettings.WindowTop = Top;
            }

            _appSettings.WindowState = WindowState;

            // 現在のサムネイルサイズを保存
            _appSettings.ThumbnailSize = (int)ThumbnailSizeSlider.Value;

            // 現在のフォルダパスを保存
            _appSettings.LastFolderPath = _thumbnailLoader.CurrentFolderPath;

            // 現在の選択ファイルパスを保存
            _appSettings.LastSelectedFilePath = _currentSelectedFilePath;

            // 設定を保存
            SettingsHelper.SaveSettings(_appSettings);
        }

        private async void LoadDrivesAsync()
        {
            var drives = await FileSystemHelper.LoadDrivesAsync();
            foreach (var drive in drives)
            {
                await Dispatcher.InvokeAsync(() => FolderTreeView.Items.Add(FileSystemHelper.CreateDriveNode(drive)));
            }
        }

        private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem { Tag: string path } selectedItem && path != null)
            {
                if (Directory.Exists(path))
                {
                    // フォルダが選択された場合
                    ClearPropertiesDisplay(); // プロパティ表示をクリア
                    // 新しいフォルダのサムネイルを読み込み
                    _thumbnailLoader.LoadFileNodes(path);

                    // fileNodesLoadedイベントでフォルダ選択時の先頭アイテム選択を行うためのフラグをセット
                    _shouldSelectFirstItem = true;
                }
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
                _thumbnailLoader.ThumbnailSize = newSize;
        }


        /// <summary>
        /// サムネイルのロード完了時に前回選択したファイルを選択する処理
        /// </summary>
        private async void OnFileNodesLoaded(object? sender, EventArgs e)
        {
            try
            {
                // UIスレッドでの処理を確実にするため
                await Task.Delay(0);

                // アイテムがない場合は何もしない
                if (_viewModel.Items.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No items to select");
                    return;
                }

                if (!_isFirstLoaded && !string.IsNullOrEmpty(_appSettings.LastSelectedFilePath) &&
                    File.Exists(_appSettings.LastSelectedFilePath))
                {
                    _isFirstLoaded = true;
                    SelectThumbnail(_appSettings.LastSelectedFilePath);
                    // サムネイルにフォーカスを設定
                    await Task.Delay(50);
                    ThumbnailItemsControl.Focus();
                }
                else if (_shouldSelectFirstItem)
                {
                    _shouldSelectFirstItem = false;
                    var firstItem = _viewModel.Items[0];

                    // 先頭アイテムを選択
                    _currentSelectedFilePath = firstItem.FullPath;
                    _viewModel.SelectedItem = firstItem;
                    LoadFilePropertiesAsync(firstItem.FullPath);

                    // サムネイルにフォーカスを設定
                    await Task.Delay(50);
                    ThumbnailItemsControl.Focus();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnFileNodesLoaded: {ex.Message}");
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

            var matchingItem = _viewModel.Items.FirstOrDefault(x => x.FullPath == filePath);
            if (matchingItem != null)
            {
                _currentSelectedFilePath = filePath;
                _viewModel.SelectedItem = matchingItem;
                ThumbnailItemsControl.ScrollIntoView(matchingItem);
                LoadFilePropertiesAsync(filePath);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No matching item found");
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

        private async void ThumbnailItemsControl_Loaded(object sender, RoutedEventArgs e)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
            if (scrollViewer != null)
            {
                // 実際のScrollViewerにイベントハンドラを直接登録
                scrollViewer.ScrollChanged += OnScrollChanged;
                await LoadVisibleThumbnailsAsync(scrollViewer);
            }

            // キーボードナビゲーションのイベントハンドラを追加
            ThumbnailItemsControl.KeyDown += ThumbnailItemsControl_KeyDown;

            // ListViewの選択状態が変更されたときのイベントハンドラを追加
            ThumbnailItemsControl.SelectionChanged += (s, args) =>
            {
                if (args.AddedItems.Count > 0 && args.AddedItems[0] is FileNodeModel selectedItem)
                {
                    _currentSelectedFilePath = selectedItem.FullPath;
                    _viewModel.SelectedItem = selectedItem;
                    LoadFilePropertiesAsync(selectedItem.FullPath);
                }
            };

            // ViewModelのSelectedItemプロパティを監視
            _viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(_viewModel.SelectedItem) && _viewModel.SelectedItem != null)
                {
                    // ViewModelの選択が変更されたら、ListViewの選択も同期
                    if (ThumbnailItemsControl.SelectedItem != _viewModel.SelectedItem)
                    {
                        ThumbnailItemsControl.SelectedItem = _viewModel.SelectedItem;
                    }
                    LoadFilePropertiesAsync(_viewModel.SelectedItem.FullPath);
                }
            };
        }

        private async Task LoadVisibleThumbnailsAsync(ScrollViewer scrollViewer)
        {
            // スロットリング - 短時間に何度も呼び出さない
            lock (_loadLock)
            {
                var now = DateTime.Now;
                var timeSinceLastLoad = now - _lastLoadTime;
                if (timeSinceLastLoad.TotalMilliseconds < 200) // 200ms未満の間隔では読み込まない
                {
                    return;
                }
                _lastLoadTime = now;
            }

            // ItemsControl が初期化されるのを待つ
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

            DependencyObject parent = VisualTreeHelper.GetParent(scrollViewer);
            while (parent != null && !(parent is ItemsControl))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            var itemsControl = parent as ItemsControl;
            if (itemsControl == null || itemsControl.Items.Count == 0) return;

            // 可視範囲の取得を試みる
            int firstVisibleIndex = 0;
            int lastVisibleIndex = 0;

            // まず表示されているアイテムのインデックスを取得しようとする
            bool indexesFound = false;

            for (int i = 0; i < itemsControl.Items.Count; i++)
            {
                var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container != null)
                {
                    // コンテナが見つかった場合、スクロール位置から表示範囲を推定
                    double verticalOffset = scrollViewer.VerticalOffset;
                    double viewportHeight = scrollViewer.ViewportHeight;
                    double totalHeight = scrollViewer.ExtentHeight;
                    int totalItems = itemsControl.Items.Count;

                    // スクロール位置の比率から中央のインデックスを推定
                    double scrollRatio = verticalOffset / (totalHeight - viewportHeight);
                    int centerIndex = (int)(scrollRatio * totalItems);

                    // 可視範囲の推定サイズ（表示されているアイテムの数）
                    double containerHeight = container.ActualHeight + container.Margin.Top + container.Margin.Bottom;
                    int visibleItemCount = containerHeight > 0 ? (int)(viewportHeight / containerHeight) : 20;

                    // 中央から前後に範囲を設定
                    firstVisibleIndex = Math.Max(0, centerIndex - visibleItemCount / 2);
                    lastVisibleIndex = Math.Min(totalItems - 1, centerIndex + visibleItemCount / 2);

                    indexesFound = true;
                    break;
                }
            }

            // コンテナが見つからない場合はデフォルト値で推定
            if (!indexesFound)
            {
                double verticalOffset = scrollViewer.VerticalOffset;
                double viewportHeight = scrollViewer.ViewportHeight;
                double totalHeight = scrollViewer.ExtentHeight;
                int totalItems = itemsControl.Items.Count;

                // スクロール位置の比率から表示範囲を推定
                double scrollRatio = totalHeight > 0 ? verticalOffset / totalHeight : 0;
                int itemsPerScreen = Math.Min(50, totalItems / 10); // 画面あたりのアイテム数を推定

                firstVisibleIndex = (int)(scrollRatio * (totalItems - itemsPerScreen));
                lastVisibleIndex = Math.Min(totalItems - 1, firstVisibleIndex + itemsPerScreen);
            }

            // 前後に固定数のバッファを追加
            int bufferSize = 100; // 前後に100アイテムずつ追加読み込み

            int extendedFirstIndex = Math.Max(0, firstVisibleIndex - bufferSize);
            int extendedLastIndex = Math.Min(itemsControl.Items.Count - 1, lastVisibleIndex + bufferSize);

            // 拡張した範囲でサムネイルをロード
            await _thumbnailLoader.LoadMoreThumbnailsAsync(extendedFirstIndex, extendedLastIndex);
        }

        // ヘルパーメソッド: 子要素を検索
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

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

        /// <summary>
        /// サムネイルがクリックされたときの処理
        /// </summary>
        private void Thumbnail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FileNodeModel fileNode)
            {
                // ViewModelのSelectedItemを更新
                _viewModel.SelectedItem = fileNode;
                _currentSelectedFilePath = fileNode.FullPath;

                // 親のListViewにフォーカスを与える
                ThumbnailItemsControl.Focus();

                // プロパティを表示
                LoadFilePropertiesAsync(fileNode.FullPath);
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

        private void ShowImageViewer(string filePath)
        {
            try
            {
                var viewer = new ImageViewerWindow(filePath);
                viewer.Owner = this;
                viewer.ParentWindow = this; // 親ウィンドウへの参照を設定
                _currentViewerWindow = viewer; // 現在開いているビューアを追跡
                viewer.Show();
                viewer.Focus(); // ビューアウィンドウにフォーカスを設定
            }
            catch (Exception ex)
            {
                MessageBox.Show($"画像の表示中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 指定されたファイルパスの前の画像ファイルパスを取得します
        /// </summary>
        /// <param name="currentFilePath">現在の画像ファイルパス</param>
        /// <returns>前の画像のファイルパス、存在しない場合はnull</returns>
        public string? GetPreviousImage(string currentFilePath)
        {
            if (_viewModel.Items.Count <= 1)
                return null;

            // 現在の画像のインデックスを検索
            var currentIndex = _viewModel.Items.ToList().FindIndex(i => i.FullPath == currentFilePath);
            if (currentIndex < 0)
                return null;

            // 前のインデックスを計算（リストの最初の場合は最後に循環）
            var prevIndex = (currentIndex > 0) ? currentIndex - 1 : _viewModel.Items.Count - 1;
            return _viewModel.Items[prevIndex].FullPath;
        }

        /// <summary>
        /// 指定されたファイルパスの次の画像ファイルパスを取得します
        /// </summary>
        /// <param name="currentFilePath">現在の画像ファイルパス</param>
        /// <returns>次の画像のファイルパス、存在しない場合はnull</returns>
        public string? GetNextImage(string currentFilePath)
        {
            if (_viewModel.Items.Count <= 1)
                return null;

            // 現在の画像のインデックスを検索
            var currentIndex = _viewModel.Items.ToList().FindIndex(i => i.FullPath == currentFilePath);
            if (currentIndex < 0)
                return null;

            // 次のインデックスを計算（リストの最後の場合は最初に循環）
            var nextIndex = (currentIndex < _viewModel.Items.Count - 1) ? currentIndex + 1 : 0;
            return _viewModel.Items[nextIndex].FullPath;
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

        // メニュー関連のメソッド
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SmallThumbnailMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ThumbnailSizeSlider.Value = 80;
        }

        private void MediumThumbnailMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ThumbnailSizeSlider.Value = 120;
        }

        private void LargeThumbnailMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ThumbnailSizeSlider.Value = 240;
        }

        private void RefreshMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // 現在のフォルダを再読み込み
            if (!string.IsNullOrEmpty(_thumbnailLoader.CurrentFolderPath))
            {
                _thumbnailLoader.LoadFileNodes(_thumbnailLoader.CurrentFolderPath);
            }
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // 設定ウィンドウを表示
            var settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this;

            if (settingsWindow.ShowDialog() == true)
            {
                // 設定が変更された場合は反映
                ApplySettings();
            }
        }

        /// <summary>
        /// 設定をUIに適用する
        /// </summary>
        private void ApplySettings()
        {
            // 設定を再読み込み
            _appSettings = SettingsHelper.GetSettings();

            // サムネイルサイズを設定
            ThumbnailSizeSlider.Value = _appSettings.ThumbnailSize;
        }

        /// <summary>
        /// マウスホイールでサムネイルをスクロールする処理
        /// </summary>
        private void ThumbnailItemsControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
            if (scrollViewer != null)
            {
                // 基本のスクロール動作を維持しつつ、倍率を適用
                e.Handled = true;
                scrollViewer.ScrollToVerticalOffset(
                    scrollViewer.VerticalOffset - (e.Delta * _appSettings.MouseWheelMultiplier / 3));
            }
        }
    }
}
