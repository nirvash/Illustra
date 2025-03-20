using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.ComponentModel;
using System.Windows.Media;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.Events;
using System.Windows.Media.Animation;
using Illustra.Helpers.Interfaces;
using Illustra.Functions;
using MahApps.Metro.Controls;
using System.Windows.Controls.Primitives;

namespace Illustra.Views
{
    public partial class ImageViewerWindow : MetroWindow, INotifyPropertyChanged
    {
        // フルスクリーン切り替え前のウィンドウ状態を保存
        private WindowState _previousWindowState;
        private WindowStyle _previousWindowStyle;
        private bool _isFullScreen = false;

        public event EventHandler? IsFullscreenChanged;

        public bool IsFullScreen
        {
            get => _isFullScreen;
            private set
            {
                if (_isFullScreen != value)
                {
                    _isFullScreen = value;
                    base.ShowTitleBar = !value; // フルスクリーン時はタイトルバーを非表示
                    OnPropertyChanged(nameof(IsFullScreen));
                    IsFullscreenChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private readonly DispatcherTimer _titleBarTimer;
        private const double TITLE_BAR_SHOW_AREA = 100; // マウスがここまで近づいたらタイトルバーを表示

        // 画像切り替え用
        private string _currentFilePath;
        private bool _isSlideshowActive = false;
        private readonly DispatcherTimer _slideshowTimer;
        public new ThumbnailListControl? Parent { get; set; }
        private DatabaseManager? _dbManager;

        private ImagePropertiesModel _properties = new();
        public ImagePropertiesModel Properties
        {
            get => _properties;
            private set
            {
                if (_properties != value)
                {
                    var oldProperties = _properties;
                    _properties = value;

                    // Ratingプロパティの変更を監視するためのハンドラーを設定
                    if (oldProperties != null)
                    {
                        oldProperties.PropertyChanged -= Properties_PropertyChanged;
                    }
                    if (_properties != null)
                    {
                        _properties.PropertyChanged += Properties_PropertyChanged;
                    }
                    // 明示的に変更を通知
                    OnPropertyChanged(nameof(Properties));
                    OnPropertyChanged(nameof(Properties.Rating));
                }
            }
        }

        private void Properties_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ImagePropertiesModel.Rating))
            {
                // Ratingが変更された時、改めてPropertiesの変更を通知
                OnPropertyChanged(nameof(Properties));
                System.Diagnostics.Debug.WriteLine($"Rating changed to: {Properties.Rating}");
            }
        }

        public string FileName { get; private set; }
        public BitmapSource? ImageSource { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private DispatcherTimer hideCursorTimer;
        private readonly IImageCache _imageCache;

        public ImageViewerWindow(string filePath)
        {
            InitializeComponent();
            FileName = System.IO.Path.GetFileName(filePath);
            _currentFilePath = filePath;
            DataContext = this;

            // キャッシュの初期化
            _imageCache = new WindowBasedImageCache();
            _dbManager = ContainerLocator.Container.Resolve<DatabaseManager>();

            // スライドショータイマーの初期化
            var viewerSettings = ViewerSettingsHelper.LoadSettings();
            _slideshowTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(viewerSettings.SlideshowIntervalSeconds)
            };
            _slideshowTimer.Tick += (s, e) =>
            {
                NavigateToNextImage();
            };

            // イベントの購読
            var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            eventAggregator?.GetEvent<RatingChangedEvent>()?.Subscribe(OnRatingChanged);

            this.StateChanged += MainWindow_StateChanged;

            // マウスカーソル非表示用のタイマー
            hideCursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            hideCursorTimer.Tick += (s, args) =>
            {
                Mouse.OverrideCursor = Cursors.None;
                hideCursorTimer.Stop();
            };

            // ウィンドウの状態を復元
            var settings = ViewerSettingsHelper.LoadSettings();

            // フルスクリーン設定を保存して、Loaded後に適用
            IsFullScreen = settings.IsFullScreen; // プロパティ経由で設定

            // プロパティパネルの表示状態を設定
            PropertyPanel.Visibility = settings.VisiblePropertyPanel ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            PropertySplitter.Visibility = settings.VisiblePropertyPanel ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            // フルスクリーン状態に応じた幅を読み込む
            _lastPropertyPanelWidth = settings.IsFullScreen
                ? settings.FullScreenPropertyColumnWidth
                : settings.NormalPropertyColumnWidth;

            // プロパティパネル列の幅を設定
            if (!settings.VisiblePropertyPanel)
            {
                MainGrid.ColumnDefinitions[1].Width = new GridLength(0);
                MainGrid.ColumnDefinitions[2].Width = new GridLength(0);
            }
            else if (_lastPropertyPanelWidth > 0)
            {
                MainGrid.ColumnDefinitions[1].Width = new GridLength(3);  // スプリッター
                MainGrid.ColumnDefinitions[2].Width = new GridLength(_lastPropertyPanelWidth);
            }

            // ウィンドウが表示された後に実行する処理
            Loaded += (s, e) => OnWindowLoaded();
        }



        private async Task LoadImagePropertiesAsync(string filePath)
        {
            try
            {
                // ファイルからプロパティを読み込む
                var newProperties = await ImagePropertiesModel.LoadFromFileAsync(filePath);

                // データベースからレーティングを読み込んで設定
                var fileNode = await _dbManager.GetFileNodeAsync(filePath);
                if (fileNode != null)
                {
                    newProperties.Rating = fileNode.Rating;
                }

                // プロパティの更新はUIスレッドで行う
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // まず新しいプロパティインスタンスをセット（これによりPropertyChangedイベントハンドラが設定される）
                    Properties = newProperties;
                    _currentFilePath = filePath;
                    FileName = System.IO.Path.GetFileName(filePath);

                    // PropertyPanelControlにプロパティを設定
                    if (PropertyPanelControl != null)
                    {
                        PropertyPanelControl.ImageProperties = Properties;
                        PropertyPanelControl.DataContext = Properties;
                    }

                    OnPropertyChanged(nameof(ImageSource));
                    OnPropertyChanged(nameof(FileName));
                    OnPropertyChanged(nameof(Properties));
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image properties: {ex.Message}");
                MessageBox.Show($"画像プロパティの読み込みに失敗しました：{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnWindowLoaded()
        {
            // キャッシュから画像を読み込み（なければ新規作成）
            LoadAndDisplayImage(_currentFilePath);

            // プロパティの読み込み
            await LoadImagePropertiesAsync(_currentFilePath);

            // プロパティパネルにプロパティを設定
            PropertyPanelControl.OnFileSelected(new SelectedFileModel("", _currentFilePath, Properties.Rating));
            /*
                        // フルスクリーン状態を設定
                        if (_isFullScreen)
                        {
                            this.ShowTitleBar = false; // フルスクリーン時はタイトルバーを非表示
                            _previousWindowState = WindowState;
                            _previousWindowStyle = WindowStyle;

                            // フルスクリーン状態にする
                            WindowStyle = WindowStyle.None;
                            WindowState = WindowState.Maximized;

                            // 確実に設定が有効になるように少し待つ
                            await Task.Delay(50);
                        }
            */
            // フォーカスを確実に設定
            await Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                Activate(); // ウィンドウをアクティブにする
                Focus();    // ウィンドウにフォーカスを設定
                MainImage.Focus(); // 画像にフォーカス
            }));

            // 前後の画像をキャッシュ
            var viewModel = Parent?.GetViewModel();
            if (viewModel != null)
            {
                var files = viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
                var currentIndex = files.FindIndex(f => f.FullPath == _currentFilePath);
                if (currentIndex >= 0)
                {
                    _imageCache.UpdateCache(files, currentIndex);
                }
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab)
            {
                // `PropertyPanel` 内にフォーカスがあるかチェック
                if (IsDescendantOfPropertyPanel(Keyboard.FocusedElement as DependencyObject, PropertyPanel))
                {
                    e.Handled = true; // `Tab` の通常動作を無効化
                    // フォーカスを解除
                    FocusManager.SetFocusedElement(this, null);
                    Keyboard.ClearFocus();
                    this.Focus(); // フォーカスをウィンドウに戻す
                }
            }
        }

        private bool IsDescendantOfPropertyPanel(DependencyObject target, DependencyObject parent)
        {
            while (target != null)
            {
                if (target == parent)
                {
                    return true;
                }
                target = VisualTreeHelper.GetParent(target);
            }
            return false;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            var shortcutHandler = KeyboardShortcutHandler.Instance;

            if (e.Key == Key.Tab)
            {
                e.Handled = true; // `Tab` キーでのフォーカス移動を抑止
                return;
            }

            // 各機能のショートカットをチェック
            if (shortcutHandler.IsShortcutMatch(FuncId.CloseViewer, e.Key))
            {
                CloseViewer();
                e.Handled = true;
            }
            else if (shortcutHandler.IsShortcutMatch(FuncId.ToggleFullScreen, e.Key))
            {
                ToggleFullScreen();
                e.Handled = true;
            }
            else if (shortcutHandler.IsShortcutMatch(FuncId.ToggleSlideshow, e.Key))
            {
                ToggleSlideshow();
                e.Handled = true;
            }
            else if (shortcutHandler.IsShortcutMatch(FuncId.IncreaseSlideshowInterval, e.Key))
            {
                AdjustSlideshowInterval(0.1);
                e.Handled = true;
            }
            else if (shortcutHandler.IsShortcutMatch(FuncId.DecreaseSlideshowInterval, e.Key))
            {
                AdjustSlideshowInterval(-0.1);
                e.Handled = true;
            }
            else if (shortcutHandler.IsShortcutMatch(FuncId.PreviousImage, e.Key))
            {
                NavigateToPreviousImage();
                e.Handled = true;
            }
            else if (shortcutHandler.IsShortcutMatch(FuncId.NextImage, e.Key))
            {
                NavigateToNextImage();
                e.Handled = true;
            }
            else if (shortcutHandler.IsShortcutMatch(FuncId.TogglePropertyPanel, e.Key))
            {
                TogglePropertyPanel();
                e.Handled = true;
            }
            else if (shortcutHandler.IsShortcutMatch(FuncId.Delete, e.Key))
            {
                DeleteCurrentImage();
                e.Handled = true;
            }
            else if (shortcutHandler.IsShortcutMatch(FuncId.MoveToStart, e.Key))
            {
                NavigateToFirstImage();
                e.Handled = true;
            }
            else if (shortcutHandler.IsShortcutMatch(FuncId.MoveToEnd, e.Key))
            {
                NavigateToLastImage();
                e.Handled = true;
            }

            // レーティング設定
            for (int i = 0; i <= 5; i++)
            {
                if (shortcutHandler.IsShortcutMatch(FuncId.Ratings[i], e.Key))
                {
                    SetRating(i);
                    e.Handled = true;
                    break;
                }
            }
        }

        private async void SetRating(int rating)
        {
            if (Properties == null || string.IsNullOrEmpty(_currentFilePath)) return;

            // 同じレーティングの場合はクリア
            if (Properties.Rating == rating && rating != 0)
            {
                rating = 0;
            }

            // 現在の値と異なる場合のみ処理を実行
            if (Properties.Rating != rating)
            {
                // レーティングを更新
                Properties.Rating = rating;

                // レーティング変更イベントを発行. レーティングの永続化は受信先で行う
                var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                eventAggregator?.GetEvent<RatingChangedEvent>()?.Publish(
                    new RatingChangedEventArgs { FilePath = _currentFilePath, Rating = rating });
            }
        }

        private void ShowNotification(string message, int fontSize = 24)
        {
            NotificationText.Text = message;
            NotificationText.FontSize = fontSize;
            var storyboard = (Storyboard)FindResource("ShowNotificationStoryboard");
            storyboard.Begin(Notification);
        }

        private void AdjustSlideshowInterval(double adjustment)
        {
            var settings = ViewerSettingsHelper.LoadSettings();
            // 0.1秒単位に丸める
            var rawInterval = settings.SlideshowIntervalSeconds + adjustment;
            var newInterval = Math.Max(0.1, Math.Round(rawInterval * 10) / 10);
            settings.SlideshowIntervalSeconds = newInterval;
            ViewerSettingsHelper.SaveSettings(settings);

            // タイマーの間隔を更新
            _slideshowTimer.Interval = TimeSpan.FromSeconds(newInterval);

            // 通知を表示
            ShowNotification(string.Format(
                (string)FindResource("String_Slideshow_IntervalFormat"),
                newInterval), 32);
        }

        private void ToggleSlideshow()
        {
            if (_isSlideshowActive)
            {
                _slideshowTimer.Stop();
                _isSlideshowActive = false;
                ShowNotification((string)FindResource("String_Slideshow_PauseIcon"), 48);
            }
            else
            {
                _slideshowTimer.Start();
                _isSlideshowActive = true;
                ShowNotification((string)FindResource("String_Slideshow_PlayIcon"), 48);
            }
        }

        private void TogglePropertyPanel()
        {
            if (PropertyPanel.Visibility == System.Windows.Visibility.Visible)
            {
                // プロパティパネル・スプリッターを非表示にする前に現在の幅を保存
                _lastPropertyPanelWidth = MainGrid.ColumnDefinitions[2].ActualWidth;

                // 幅が0以下の場合はデフォルト値を設定
                if (_lastPropertyPanelWidth <= 0)
                {
                    _lastPropertyPanelWidth = 250;
                }

                // プロパティパネル・スプリッターを非表示にする
                PropertyPanel.Visibility = System.Windows.Visibility.Collapsed;
                PropertySplitter.Visibility = System.Windows.Visibility.Collapsed;

                // カラムの幅を0に設定
                MainGrid.ColumnDefinitions[1].Width = new System.Windows.GridLength(0);
                MainGrid.ColumnDefinitions[2].Width = new System.Windows.GridLength(0);
            }
            else
            {
                // プロパティパネルを表示する
                PropertyPanel.Visibility = System.Windows.Visibility.Visible;
                PropertySplitter.Visibility = System.Windows.Visibility.Visible;

                // カラムの幅を復元
                MainGrid.ColumnDefinitions[1].Width = new System.Windows.GridLength(3);  // スプリッター

                // 保存していた幅または設定から幅を取得
                double panelWidth = _lastPropertyPanelWidth;

                // 幅が0以下の場合は、設定から取得または既定値を使用
                if (panelWidth <= 0)
                {
                    var settings = ViewerSettingsHelper.LoadSettings();
                    panelWidth = _isFullScreen
                        ? (settings.FullScreenPropertyColumnWidth > 0 ? settings.FullScreenPropertyColumnWidth : 250)
                        : (settings.NormalPropertyColumnWidth > 0 ? settings.NormalPropertyColumnWidth : 250);
                }

                MainGrid.ColumnDefinitions[2].Width = new System.Windows.GridLength(panelWidth);
            }

            // 設定を保存
            SaveCurrentSettings();
        }

        private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            // プロパティパネルのサイズ変更時に幅を保存
            if (PropertyPanel.Visibility == System.Windows.Visibility.Visible)
            {
                _lastPropertyPanelWidth = MainGrid.ColumnDefinitions[2].ActualWidth;
            }
            SaveCurrentSettings();
        }

        // 前回のプロパティパネル幅を保存するフィールド
        private double _lastPropertyPanelWidth = 250;

        // 前の画像に移動
        private void NavigateToPreviousImage()
        {
            if (Parent == null) return;

            // 親ウィンドウに前の画像への移動をリクエスト
            string? previousFilePath = Parent.GetPreviousImage(_currentFilePath);
            if (!string.IsNullOrEmpty(previousFilePath))
            {
                _ = SwitchToImage(previousFilePath);
            }
        }

        // 次の画像に移動
        private void NavigateToNextImage()
        {
            if (Parent == null) return;

            // 親ウィンドウに次の画像への移動をリクエスト
            string? nextFilePath = Parent.GetNextImage(_currentFilePath);
            if (!string.IsNullOrEmpty(nextFilePath))
            {
                _ = SwitchToImage(nextFilePath);
            }
            else if (_isSlideshowActive)
            {
                // 次の画像がない場合はスライドショーを停止
                _slideshowTimer.Stop();
                _isSlideshowActive = false;
                ShowNotification((string)FindResource("String_Slideshow_PauseIcon"), 48);
            }
        }
        private void LoadAndDisplayImage(string filePath)
        {
            try
            {
                /*              キャッシュ動作の解析用ログ
                                var viewModel = Parent?.GetViewModel();
                                if (viewModel != null)
                                {
                                    var files = viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
                                    var currentIndex = files.FindIndex(f => f.FullPath == filePath);
                                    bool isFromCache = _imageCache.HasImage(filePath);

                                    // キャッシュされているファイルのインデックスを取得
                                    var cachedIndexes = _imageCache.CachedItems.Keys
                                        .Select(p => files.FindIndex(f => f.FullPath == p))
                                        .Where(i => i >= 0)
                                        .OrderBy(i => i);

                                    // キャッシュの状態を詳細にログ出力
                                    LogHelper.LogWithTimestamp(
                                        $"Loading image [index: {currentIndex}] from {(isFromCache ? "cache" : "disk")}\n" +
                                        $"Cached indexes: [{string.Join(", ", cachedIndexes)}]",
                                        LogHelper.Categories.ImageCache);
                                }
                */
                ImageSource = _imageCache.GetImage(filePath);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"画像の読み込み中にエラーが発生: {ex.Message}", ex);
                throw;
            }
        }

        // 新しい画像を読み込む
        private async Task SwitchToImage(string filePath)
        {
            try
            {
                // 1. 現在のファイルパスを更新
                _currentFilePath = filePath;

                // 2. まず現在の画像を表示（キャッシュミス時は自動で読み込まれる）
                LoadAndDisplayImage(filePath);

                // 3. 画像のプロパティを読み込み
                await LoadImagePropertiesAsync(filePath);

                // 4. 前後の画像をキャッシュ
                var viewModel = Parent?.GetViewModel();
                if (viewModel != null)
                {
                    var files = viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
                    var currentIndex = files.FindIndex(f => f.FullPath == filePath);
                    if (currentIndex >= 0)
                    {
                        // キャッシュ更新前のインデックスを取得
                        var cachedIndexesBefore = _imageCache.CachedItems.Keys
                            .Select(p => files.FindIndex(f => f.FullPath == p))
                            .Where(i => i >= 0)
                            .OrderBy(i => i);

                        LogHelper.LogWithTimestamp(
                            $"UpdateCache: Current index = {currentIndex}\n" +
                            $"Cached indexes before: [{string.Join(", ", cachedIndexesBefore)}]",
                            LogHelper.Categories.ImageCache);

                        _imageCache.UpdateCache(files, currentIndex);

                        // キャッシュ更新後のインデックスを取得
                        var cachedIndexesAfter = _imageCache.CachedItems.Keys
                            .Select(p => files.FindIndex(f => f.FullPath == p))
                            .Where(i => i >= 0)
                            .OrderBy(i => i);

                        LogHelper.LogWithTimestamp(
                            $"UpdateCache: After update - Current index = {currentIndex}\n" +
                            $"Cached indexes after: [{string.Join(", ", cachedIndexesAfter)}]",
                            LogHelper.Categories.ImageCache);
                    }
                }

                // 親ウィンドウのサムネイル選択を更新
                Parent?.SyncThumbnailSelection(filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image: {ex.Message}");
                MessageBox.Show($"画像の読み込みに失敗しました：{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // クローズ処理を共通メソッドに委託
            CloseViewer();
        }

        private void CloseViewer()
        {
            // フルスクリーン中は状態を保存してから閉じる
            if (_isFullScreen)
            {
                // 設定を明示的に保存（共通メソッド使用）
                SaveCurrentSettings();
            }

            Close();
        }

        private void MainWindow_StateChanged(object sender, System.EventArgs e)
        {
            if (this.WindowState == WindowState.Maximized && this.WindowStyle == WindowStyle.None)
            {
                base.ShowTitleBar = false;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                IsFullScreen = true; // プロパティ経由で設定
            }
            else if (this.WindowState == WindowState.Normal)
            {
                base.ShowTitleBar = true; // タイトルバーを表示
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                IsFullScreen = false; // プロパティ経由で設定

                // マウスカーソルを表示状態に戻す
                Mouse.OverrideCursor = Cursors.Arrow;
                hideCursorTimer.Stop();
            }

            // フルスクリーン状態に応じた幅を読み込む
            var settings = ViewerSettingsHelper.LoadSettings();
            _lastPropertyPanelWidth = _isFullScreen
                ? settings.FullScreenPropertyColumnWidth
                : settings.NormalPropertyColumnWidth;

            // プロパティパネル列の幅を設定
            if (!settings.VisiblePropertyPanel)
            {
                MainGrid.ColumnDefinitions[1].Width = new GridLength(0);
                MainGrid.ColumnDefinitions[2].Width = new GridLength(0);
            }
            else if (_lastPropertyPanelWidth > 0)
            {
                MainGrid.ColumnDefinitions[1].Width = new GridLength(3);  // スプリッター
                MainGrid.ColumnDefinitions[2].Width = new GridLength(_lastPropertyPanelWidth);
            }
        }

        // フルスクリーン切り替えボタンのクリックイベント
        private void ToggleFullScreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        // キーショートカットからのフルスクリーン切り替え
        private void ToggleFullScreen()
        {
            if (!_isFullScreen)
            {
                // フルスクリーンに切り替え
                base.ShowTitleBar = false;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                IsFullScreen = true; // プロパティ経由で設定
            }
            else
            {
                // 保存していた状態に戻す
                base.ShowTitleBar = true; // タイトルバーを表示
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                IsFullScreen = false; // プロパティ経由で設定
            }
        }

        // 現在のウィンドウ設定を保存する共通メソッド
        private void SaveCurrentSettings(bool isSwitchgingFullScreen = false, bool wasFullScreen = false)
        {
            // プロパティパネルの幅を取得（非表示の場合は前回保存した値を使用）
            double propertyWidth = PropertyPanel.Visibility == Visibility.Visible
                ? MainGrid.ColumnDefinitions[2].ActualWidth
                : _lastPropertyPanelWidth;

            var settings = ViewerSettingsHelper.LoadSettings();
            settings.IsFullScreen = _isFullScreen;
            settings.VisiblePropertyPanel = PropertyPanel.Visibility == Visibility.Visible;

            // フルスクリーン状態に応じて適切な幅を保存
            var isFullScreenForSave = isSwitchgingFullScreen ? wasFullScreen : _isFullScreen;
            if (isFullScreenForSave)
            {
                settings.FullScreenPropertyColumnWidth = propertyWidth > 0 ? propertyWidth : 250;
            }
            else
            {
                settings.NormalPropertyColumnWidth = propertyWidth > 0 ? propertyWidth : 250;
            }
            ViewerSettingsHelper.SaveSettings(settings);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // スライドショーが実行中なら停止
                if (_isSlideshowActive)
                {
                    _slideshowTimer.Stop();
                    _isSlideshowActive = false;
                }

                // 閉じる過程での最初の段階でフルスクリーン状態を保存
                // 共通メソッドを使用して設定を保存
                SaveCurrentSettings();

                // タイマーをキャンセルしてマウスカーソルを表示状態に戻す
                Mouse.OverrideCursor = Cursors.Arrow;
                hideCursorTimer.Stop();

                // 画像リソースの解放
                ImageSource = null;

                // キャッシュをクリア
                _imageCache.Clear();

                // 明示的なGCを実行
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Closing error: {ex.Message}");
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // OnClosingで既に保存したので、ここでは何もしない

            // サムネイルリストにフォーカスを設定
            Parent?.FocusSelectedThumbnail();
        }

        private void MainImage_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
            {
                // ホイール上回転で前の画像
                NavigateToPreviousImage();
            }
            else
            {
                // ホイール下回転で次の画像
                NavigateToNextImage();
            }
            e.Handled = true;
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // クリックされた要素を取得
            var clickedElement = e.OriginalSource as DependencyObject;

            // `PropertyPanel` 内がクリックされたかチェック
            if (clickedElement != null && IsDescendantOf(clickedElement, PropertyPanel))
            {
                return; // `PropertyPanel` 内なら何もしない
            }

            // フォーカスを解除
            FocusManager.SetFocusedElement(this, null);
            Keyboard.ClearFocus();
            this.Focus();
        }

        private bool IsDescendantOf(DependencyObject target, DependencyObject parent)
        {
            while (target != null)
            {
                if (target == parent)
                {
                    return true;
                }
                target = VisualTreeHelper.GetParent(target);
            }
            return false;
        }

        private Point? _lastMousePosition;
        private const double MOUSE_MOVEMENT_THRESHOLD = 5; // 5ピクセル以上の移動で検知

        private void MainGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isFullScreen) return;

            var currentPosition = e.GetPosition(this);

            // マウスがプロパティパネル上にある場合は、カーソルを表示したままにする
            if (e.OriginalSource is DependencyObject element && IsDescendantOf(element, PropertyPanel))
            {
                Mouse.OverrideCursor = null;
                hideCursorTimer.Stop();
                _lastMousePosition = currentPosition;
                return;
            }

            // 前回位置がない場合は現在位置を保存して終了
            if (!_lastMousePosition.HasValue)
            {
                _lastMousePosition = currentPosition;
                return;
            }

            // マウスの移動距離を計算
            var deltaX = Math.Abs(currentPosition.X - _lastMousePosition.Value.X);
            var deltaY = Math.Abs(currentPosition.Y - _lastMousePosition.Value.Y);
            var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

            // 一定以上の移動があった場合のみカーソルを表示
            if (distance > MOUSE_MOVEMENT_THRESHOLD)
            {
                Mouse.OverrideCursor = null; // nullに設定することでデフォルトのカーソルに戻す
                hideCursorTimer.Stop();
                hideCursorTimer.Start();
            }

            _lastMousePosition = currentPosition;
        }

        private void FullScreenButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        private async void DeleteCurrentImage()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentFilePath) || !System.IO.File.Exists(_currentFilePath))
                    return;

                // 削除前に次の画像のパスを取得
                string? nextFilePath = Parent?.GetNextImage(_currentFilePath);
                if (nextFilePath == null)
                {
                    nextFilePath = Parent?.GetPreviousImage(_currentFilePath);
                }

                var db = ContainerLocator.Container.Resolve<DatabaseManager>();
                var fileOp = new FileOperationHelper(db);

                // ファイルを削除
                await fileOp.DeleteFile(_currentFilePath);

                // 削除通知を表示
                ShowNotification((string)FindResource("String_Status_FileDeleted"), 24);

                // 親のViewModelから削除
                var viewModel = Parent?.GetViewModel();
                if (viewModel != null)
                {
                    var fileNode = viewModel.Items.FirstOrDefault(x => x.FullPath == _currentFilePath);
                    if (fileNode != null)
                    {
                        viewModel.Items.Remove(fileNode);
                    }
                }

                // 次の画像があれば表示、なければビューアを閉じる
                if (!string.IsNullOrEmpty(nextFilePath))
                {
                    _ = SwitchToImage(nextFilePath);
                }
                else
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルの削除中にエラーが発生しました: {ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // 先頭の画像に移動
        private void NavigateToFirstImage()
        {
            if (Parent == null) return;

            var viewModel = Parent.GetViewModel();
            if (viewModel != null)
            {
                var files = viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
                if (files.Any())
                {
                    // 先頭の画像に切り替え
                    _ = SwitchToImage(files.First().FullPath);
                }
            }
        }

        // 末尾の画像に移動
        private void NavigateToLastImage()
        {
            if (Parent == null) return;

            var viewModel = Parent.GetViewModel();
            if (viewModel != null)
            {
                var files = viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
                if (files.Any())
                {
                    // 末尾の画像に切り替え
                    _ = SwitchToImage(files.Last().FullPath);
                }
            }
        }

        private void OnRatingChanged(RatingChangedEventArgs args)
        {
            if (args.FilePath == _currentFilePath && Properties != null)
            {
                Properties.Rating = args.Rating;
            }
        }
    }
}
