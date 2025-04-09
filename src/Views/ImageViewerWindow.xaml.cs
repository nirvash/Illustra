using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Text;
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
using System.Threading.Tasks;
using System.IO;
using Illustra.ViewModels;
using Illustra.Controls;
using System.Windows.Documents;
using MahApps.Metro.IconPacks; // 追加

namespace Illustra.Views
{
    public partial class ImageViewerWindow : MetroWindow, INotifyPropertyChanged
    {
        /// <summary>
        /// WebPアニメーションを表示
        /// </summary>
        public async Task ShowWebpAnimation(string filePath)
        {
            WebpPlayer.Visibility = Visibility.Visible;
            await WebpPlayer.LoadWebpAsync(filePath);
        }

        private const string CONTROL_ID = "ImageViewer";
        // フルスクリーン切り替え前のウィンドウ状態を保存
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
                    OnPropertyChanged(nameof(IsFullScreen));
                    IsFullscreenChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }


        // 画像切り替え用
        private string _currentFilePath;
        private bool _isSlideshowActive = false;
        private readonly DispatcherTimer _slideshowTimer;
        public new ThumbnailListControl? Parent { get; set; }
        private DatabaseManager? _dbManager;

        private IllustraAppContext? _appContext;
        public ImagePropertiesModel Properties { get; set; } = new ImagePropertiesModel();

        // MainViewModelへの参照を追加
        public ThumbnailListViewModel? MainViewModel => _appContext?.MainViewModel;

        private BitmapSource? _imageSource = null;
        public BitmapSource? ImageSource
        {
            get => _imageSource;
            private set
            {
                if (_imageSource != value)
                {
                    _imageSource = value;
                    OnPropertyChanged(nameof(ImageSource));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private DispatcherTimer hideCursorTimer;
        private readonly IImageCache _imageCache;

        public ImageViewerWindow()
        {
            InitializeComponent();
            this.WindowPlacementSettings = new CustomPlacementSettings
            {
                SettingsIdentifier = "ImageViewerWindow"
            };
            DataContext = this;

            // キャッシュの初期化
            _imageCache = new WindowBasedImageCache();
            _dbManager = ContainerLocator.Container.Resolve<DatabaseManager>();
            _appContext = ContainerLocator.Container.Resolve<IllustraAppContext>();

            _appContext.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(_appContext.CurrentProperties))
                {
                    Properties = _appContext.CurrentProperties;
                    OnPropertyChanged(nameof(Properties));
                }
            };
            Properties = _appContext?.CurrentProperties ?? new ImagePropertiesModel();

            // スライドショータイマーの初期化
            _slideshowTimer = new DispatcherTimer();
            _slideshowTimer.Tick += (s, e) =>
            {
                NavigateToNextImage();
            };

            // イベントの購読
            var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            eventAggregator?.GetEvent<FileSelectedEvent>()?.Subscribe(OnFileSelected,
                ThreadOption.UIThread,
                false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視

            this.StateChanged += MainWindow_StateChanged;

            // 右クリックイベントを設定
            ImageZoomControl.MouseRightButtonDown += (s, e) =>
            {
                if (_appContext?.CurrentProperties?.StableDiffusionResult != null)
                {
                    ShowPromptMenu();
                    e.Handled = true;
                }
            };

            // マウスカーソル非表示用のタイマー
            hideCursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            hideCursorTimer.Tick += (s, args) =>
            {
                // フルスクリーンかつアクティブなウィンドウの場合のみカーソルを隠す
                if (!IsFullScreen || !this.IsActive)
                {
                    // カーソルが表示されているかもしれないので、タイマーは停止する
                    hideCursorTimer.Stop();
                    return;
                }

                // VideoPlayerControl が表示されていて、かつそのコントロール上にマウスがある場合は隠さない
                if (VideoPlayerControl.Visibility == Visibility.Visible && VideoPlayerControl.IsMouseOverControls)
                {
                    // カーソルを隠さず、タイマーを停止するだけ
                    hideCursorTimer.Stop();
                    return;
                }

                // 上記以外の場合で、カーソルが表示されているなら隠す
                // マウスボタンが押されておらず、コンテキストメニューも表示されておらず、カーソルが現在表示されている場合のみ隠す
                if (Mouse.LeftButton == MouseButtonState.Released &&
                    Mouse.RightButton == MouseButtonState.Released &&
                    !(ImageZoomControl.ContextMenu?.IsOpen ?? false) &&
                    Mouse.OverrideCursor != Cursors.None)
                {
                    Mouse.OverrideCursor = Cursors.None;
                }

                // タイマーは常に停止させる（再開はMouseMoveで行う）
                hideCursorTimer.Stop();
            };

            // ウィンドウの状態を復元
            var settings = ViewerSettingsHelper.LoadSettings();
            // フルスクリーン状態やウィンドウ位置は MetroWindow が管理するので、ここでは設定しない

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
            Activated += ImageViewerWindow_Activated;
            Deactivated += ImageViewerWindow_Deactivated;


            // ウィンドウが表示された後に実行する処理
            Loaded += (s, e) => OnWindowLoaded();
            Unloaded += OnWindowUnloaded();
        }

        private async void OnWindowLoaded()
        {
            // コンテンツの読み込みとキャッシュはSwitchToContentに委譲
            await SwitchToContent(_currentFilePath, true);

            // ウィンドウ固有の初期化処理のみ残す
            await Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                Activate();
                Focus();
                ImageZoomControl.Focus();
            }));

            // ウィンドウの状態設定
            MainWindow_StateChanged(null, null);
        }


        private RoutedEventHandler OnWindowUnloaded()
        {
            return (s, e) =>
            {
                // イベントの購読解除
                var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                eventAggregator?.GetEvent<FileSelectedEvent>()?.Unsubscribe(OnFileSelected);
            };
        }

        private void ShowPromptMenu()
        {
            if (_appContext?.CurrentProperties?.StableDiffusionResult == null) return;

            // コンテキストメニューを作成
            var menu = new ContextMenu();

            // プロンプトをコピー
            var copyPromptItem = new MenuItem
            {
                Header = (string)Application.Current.FindResource("String_Thumbnail_CopyPrompt")
            };
            copyPromptItem.Click += (s, e) => CopyPrompt(PromptCopyType.Positive);
            menu.Items.Add(copyPromptItem);

            // ネガティブプロンプトをコピー (存在する場合のみ)
            if (!string.IsNullOrEmpty(_appContext?.CurrentProperties?.StableDiffusionResult?.NegativePrompt))
            {
                var copyNegativePromptItem = new MenuItem
                {
                    Header = (string)Application.Current.FindResource("String_Thumbnail_CopyNegativePrompt")
                };
                copyNegativePromptItem.Click += (s, e) => CopyPrompt(PromptCopyType.Negative);
                menu.Items.Add(copyNegativePromptItem);
            }

            // プロンプト全体をコピー
            var copyAllPromptItem = new MenuItem
            {
                Header = (string)Application.Current.FindResource("String_Thumbnail_CopyAllPrompt")
            };
            copyAllPromptItem.Click += (s, e) => CopyPrompt(PromptCopyType.All);
            menu.Items.Add(copyAllPromptItem);

            // メニューを表示
            menu.PlacementTarget = ImageZoomControl;
            ImageZoomControl.ContextMenu = menu; // ContextMenu プロパティに設定
            menu.IsOpen = true;
        }

        private enum PromptCopyType { Positive, Negative, All }

        private void CopyPrompt(PromptCopyType type)
        {
            if (_appContext?.CurrentProperties?.StableDiffusionResult == null) return;

            try
            {
                string textToCopy = "";
                var result = _appContext.CurrentProperties.StableDiffusionResult;

                switch (type)
                {
                    case PromptCopyType.Positive:
                        textToCopy = result.Prompt;
                        break;
                    case PromptCopyType.Negative:
                        textToCopy = result.NegativePrompt;
                        break;
                    case PromptCopyType.All:
                        textToCopy = _appContext.CurrentProperties.UserComment; // UserComment全体をコピー
                        break;
                }

                if (!string.IsNullOrEmpty(textToCopy))
                {
                    Clipboard.SetText(textToCopy.Trim());
                    ToastNotificationHelper.ShowRelativeTo(this, (string)Application.Current.FindResource("String_Thumbnail_PromptCopied"));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プロンプトのコピー中にエラーが発生しました: {ex.Message}");
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
                Close();
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

        private void SetRating(int rating) // CS1998 Fix: Removed unnecessary async
        {
            if (Properties == null || string.IsNullOrEmpty(_currentFilePath)) return;

            // 同じレーティングの場合はクリア
            if (Properties.Rating == rating && rating != 0)
            {
                rating = 0;
            }

            // 現在の値と異なる場合のみイベントを発行
            if (Properties.Rating != rating)
            {
                // レーティング変更イベントを発行. レーティングの永続化は受信先で行う
                var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                eventAggregator?.GetEvent<RatingChangedEvent>()?.Publish(
                    new RatingChangedEventArgs { FilePath = _currentFilePath, Rating = rating });
            }
        }

        private void ShowNotification(PackIconMaterialDesignKind? iconKind = null, string? message = null, int fontSize = 32)
        {
            // いったん両方非表示にする
            NotificationIcon.Visibility = Visibility.Collapsed;
            NotificationText.Visibility = Visibility.Collapsed;

            if (iconKind.HasValue)
            {
                NotificationIcon.Kind = iconKind.Value;
                NotificationIcon.Visibility = Visibility.Visible;
                // アイコンサイズはXAMLで固定 (Width="48", Height="48")
            }
            else if (!string.IsNullOrEmpty(message))
            {
                NotificationText.Text = message;
                NotificationText.FontSize = fontSize; // テキストの場合のみフォントサイズ適用
                NotificationText.Visibility = Visibility.Visible;
            }
            else
            {
                // 両方nullなら何もしない（通知自体を表示しない）
                return;
            }

            var storyboard = (Storyboard)FindResource("ShowNotificationStoryboard");
            // 通知ボーダー自体をターゲットにする
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
            UpdateSlideshowInterval();

            // 通知を表示 (テキストのみ)
            ShowNotification(message: string.Format(
                (string)FindResource("String_Slideshow_IntervalFormat"),
                newInterval), fontSize: 32);
        }

        private void UpdateSlideshowInterval()
        {
            var settings = ViewerSettingsHelper.LoadSettings();
            _slideshowTimer.Interval = TimeSpan.FromSeconds(settings.SlideshowIntervalSeconds);
        }

        private void ToggleSlideshow()
        {
            if (_isSlideshowActive)
            {
                _slideshowTimer.Stop();
                _isSlideshowActive = false;
                ShowNotification(iconKind: PackIconMaterialDesignKind.Pause); // アイコン表示に変更
            }
            else
            {
                UpdateSlideshowInterval();
                _slideshowTimer.Start();
                _isSlideshowActive = true;
                ShowNotification(iconKind: PackIconMaterialDesignKind.PlayArrow); // アイコン表示に変更
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

                var settings = ViewerSettingsHelper.LoadSettings();

                // 保存していた幅または設定から幅を取得
                var panelWidth = _isFullScreen
                    ? (settings.FullScreenPropertyColumnWidth > 0 ? settings.FullScreenPropertyColumnWidth : 250)
                    : (settings.NormalPropertyColumnWidth > 0 ? settings.NormalPropertyColumnWidth : 250);

                MainGrid.ColumnDefinitions[2].Width = new System.Windows.GridLength(panelWidth);
            }

            // 設定を保存. この時点では ActualWidth に反映されていない
            SaveCurrentSettings(false);
        }

        private async void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            // プロパティパネルのサイズ変更時に幅を保存
            if (PropertyPanel.Visibility == System.Windows.Visibility.Visible)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                SaveCurrentSettings();
            }
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
                _ = SwitchToContent(previousFilePath, true); // Use SwitchToContent
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
                _ = SwitchToContent(nextFilePath, true); // Use SwitchToContent
            }
            else if (_isSlideshowActive)
            {
                // 次の画像がない場合はスライドショーを停止
                _slideshowTimer.Stop();
                _isSlideshowActive = false;
                ShowNotification(iconKind: PackIconMaterialDesignKind.Pause); // アイコン表示に変更
            }
        }

        // Renamed from LoadAndDisplayImage
        private async Task LoadAndDisplayContent(string filePath)
        {
            LogHelper.LogWithTimestamp("LoadAndDisplayContent - Start", LogHelper.Categories.Performance);
            // Stop video if playing
            if (VideoPlayerControl.Visibility == Visibility.Visible)
            {
                VideoPlayerControl.StopVideo();
                VideoPlayerControl.Visibility = Visibility.Collapsed;
            }

            if (FileHelper.IsVideoFile(filePath))
            {
                ShowVideo(filePath);
            }
            else // Image or Animated WebP
            {
                // Hide video player
                VideoPlayerControl.Visibility = Visibility.Collapsed;

                if (await WebPHelper.IsAnimatedWebPAsync(filePath))
                {
                    WebpPlayer.Visibility = Visibility.Visible;
                    LogHelper.LogWithTimestamp("LoadAndDisplayContent - Before LoadWebpAsync", LogHelper.Categories.Performance);
                    await WebpPlayer.LoadWebpAsync(filePath);
                    ImageZoomControl.Visibility = Visibility.Collapsed;
                    LogHelper.LogWithTimestamp("LoadAndDisplayContent - After LoadWebpAsync", LogHelper.Categories.Performance);
                }
                else
                {
                    ShowStaticImage(filePath);
                }
            }
        }

        // Keep LoadAndDisplayImage for now, maybe make private or remove later if not needed elsewhere
        private async Task LoadAndDisplayImage(string filePath)
        {
            // Hide video player if visible
            if (VideoPlayerControl.Visibility == Visibility.Visible)
            {
                VideoPlayerControl.StopVideo();
                VideoPlayerControl.Visibility = Visibility.Collapsed;
            }

            if (await WebPHelper.IsAnimatedWebPAsync(filePath))
            {
                WebpPlayer.Visibility = Visibility.Visible;
                await WebpPlayer.LoadWebpAsync(filePath);
                ImageZoomControl.Visibility = Visibility.Collapsed;
            }
            else
            {
                ShowStaticImage(filePath);
            }
        }

        private void ShowVideo(string filePath)
        {
            // Hide other controls
            ImageZoomControl.Visibility = Visibility.Collapsed;
            WebpPlayer.Visibility = Visibility.Collapsed;

            // Show video player and set source
            VideoPlayerControl.Visibility = Visibility.Visible;
            VideoPlayerControl.FilePath = filePath; // Set FilePath to trigger loading in the control
        }

        private void ShowStaticImage(string filePath)
        {
            // Hide video player if visible
            if (VideoPlayerControl.Visibility == Visibility.Visible)
            {
                VideoPlayerControl.StopVideo();
                VideoPlayerControl.Visibility = Visibility.Collapsed;
            }
            WebpPlayer.Visibility = Visibility.Collapsed;
            ImageZoomControl.Visibility = Visibility.Visible;

            try
            {
                /*
                // キャッシュ動作の解析用ログ
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

        // 新しいコンテンツを読み込む (Renamed from SwitchToImage)
        private async Task SwitchToContent(string filePath, bool notifyFileSelection)
        {
            LogHelper.LogWithTimestamp("SwitchToContent - Start", LogHelper.Categories.Performance);
            try
            {
                if (_currentFilePath?.Equals(filePath, StringComparison.OrdinalIgnoreCase) ?? false)
                {
                    // 同じファイルの場合は何もしない
                    return;
                }

                // Stop video if playing before switching content
                if (VideoPlayerControl.Visibility == Visibility.Visible)
                {
                    VideoPlayerControl.StopVideo();
                }

                hideCursorTimer.Start();

                // 1. 現在のファイルパスを更新
                _currentFilePath = filePath;

                // 2. コンテンツを表示
                LogHelper.LogWithTimestamp("SwitchToContent - Before LoadAndDisplayContent", LogHelper.Categories.Performance);
                await LoadAndDisplayContent(filePath); // Call LoadAndDisplayContent

                LogHelper.LogWithTimestamp("SwitchToContent - After LoadAndDisplayContent", LogHelper.Categories.Performance);
                // 3. 画像の場合のみズームをリセット
                if (ImageZoomControl.Visibility == Visibility.Visible)
                {
                    ImageZoomControl.ResetZoom();
                }

                // 4. MainViewModelを取得
                var viewModel = MainViewModel;
                if (viewModel != null)
                {
                    // 5. 前後のファイルをキャッシュ対象とするが、動画はキャッシュしない
                    var files = viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
                    var currentIndex = files.FindIndex(f => f.FullPath == filePath);
                    if (currentIndex >= 0)
                    {
                        // UpdateCache内で画像ファイルのみキャッシュするように修正が必要（IImageCacheの実装による）
                        // ここでは呼び出し側でチェックする例を示す
                        // _imageCache.UpdateCache(files.Where(f => FileHelper.IsImageFile(f.FullPath)).ToList(), currentIndex);
                        // もしくは、UpdateCacheメソッド自体が動画を除外するように修正する
                        _imageCache.UpdateCache(files, currentIndex); // IImageCache側で動画を除外すると仮定
                    }
                }

                // 親ウィンドウのサムネイル選択を更新
                if (notifyFileSelection)
                {
                    var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                    eventAggregator?.GetEvent<FileSelectedEvent>()?.Publish(
                        new SelectedFileModel(CONTROL_ID, filePath));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading content: {ex.Message}");
                MessageBox.Show($"コンテンツの読み込みに失敗しました：{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // VideoPlayerまたはWebpPlayerが表示されている場合は、各コントロール側のイベントで処理するため何もしない
            if (VideoPlayerControl.Visibility == Visibility.Visible || WebpPlayer.Visibility == Visibility.Visible)
            {
                return;
            }

            // VideoPlayerが表示されていない場合（画像表示など）はここで処理
            Close();
        }


        private void MainWindow_StateChanged(object? sender, System.EventArgs e) // CS8622 Fix: Make sender nullable
        {
            if (this.WindowState == WindowState.Maximized && this.WindowStyle == WindowStyle.None)
            {
                base.ShowTitleBar = false;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                // Topmost = true; // フルスクリーン時は常に最前面に表示
                IsFullScreen = true; // プロパティ経由で設定
                hideCursorTimer.Start();
            }
            else if (this.WindowState == WindowState.Normal)
            {
                base.ShowTitleBar = true; // タイトルバーを表示
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                Topmost = false; // 通常時は最前面表示を解除
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
                // ウィンドウモードに戻す
                base.ShowTitleBar = true; // タイトルバーを表示
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                IsFullScreen = false; // プロパティ経由で設定
            }
            UpdateControlsVisibility();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateControlsVisibility();
        }

        private void UpdateControlsVisibility()
        {
            if (_isFullScreen)
            {
                WindowCommands.Visibility = Visibility.Collapsed;
                FullScreenControls.Visibility = Visibility.Visible;
            }
            else
            {
                WindowCommands.Visibility = Visibility.Visible;
                FullScreenControls.Visibility = Visibility.Collapsed;
            }
        }

        // 現在のウィンドウ設定を保存する共通メソッド
        private void SaveCurrentSettings(bool savePropertyWidth = true)
        {
            var settings = ViewerSettingsHelper.LoadSettings();
            settings.IsFullScreen = _isFullScreen;
            settings.VisiblePropertyPanel = PropertyPanel.Visibility == Visibility.Visible;

            if (settings.VisiblePropertyPanel && savePropertyWidth)
            {
                // プロパティパネルの幅を取得（非表示の場合は前回保存した値を使用）
                double propertyWidth = MainGrid.ColumnDefinitions[2].ActualWidth;

                // フルスクリーン状態に応じて適切な幅を保存
                if (_isFullScreen)
                {
                    settings.FullScreenPropertyColumnWidth = propertyWidth > 0 ? propertyWidth : 250;
                }
                else
                {
                    settings.NormalPropertyColumnWidth = propertyWidth > 0 ? propertyWidth : 250;
                }
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

                var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                eventAggregator?.GetEvent<FileSelectedEvent>()?.Unsubscribe(OnFileSelected);
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
            // コントロールキーが押されている場合はズーム処理をZoomControlに任せる
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                // ZoomControlのPreviewMouseWheelイベントハンドラがズーム処理を行う
                return;
            }

            // その他のモディファイヤーキーが押されている場合もイベントを処理しない
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

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
            // GridSplitter上ならカーソル変更を優先し、タイマーをリセット
            if (e.OriginalSource is GridSplitter splitter)
            {
                if (_isFullScreen)
                {
                    Mouse.OverrideCursor = null; // GridSplitterのCursorプロパティに任せる
                    hideCursorTimer.Stop();
                    hideCursorTimer.Start(); // タイマーはリセットしておく
                    _lastMousePosition = e.GetPosition(this); // 位置も更新
                }
                // GridSplitter自体のCursorプロパティが適用されるように、以降の処理はスキップ
                return;
            }

            // フルスクリーンかつアクティブなウィンドウの場合のみ処理
            if (!IsFullScreen || !this.IsActive) return;

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

        private void ResetZoom_Click(object sender, RoutedEventArgs e)
        {
            if (ImageZoomControl.Visibility == Visibility.Visible)
            {
                // ズームをリセット
                ImageZoomControl.ResetZoom();
            }
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
                var settings = ViewerSettingsHelper.LoadSettings();
                bool moveToRecycleBin = settings.DeleteMode == FileDeleteMode.RecycleBin;
                await fileOp.DeleteFile(_currentFilePath, moveToRecycleBin);

                // 削除通知を表示（ごみ箱に移動した場合は専用メッセージ）
                var message = moveToRecycleBin
                    ? (string)FindResource("String_Status_FileMovedToRecycleBin")
                    : (string)FindResource("String_Status_FileDeleted");
                ToastNotificationHelper.ShowRelativeTo(this, message);

                // ViewModelから削除
                var viewModel = MainViewModel;
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
                    _ = SwitchToContent(nextFilePath, true);
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
            // MainViewModelを使用
            var viewModel = MainViewModel;
            if (viewModel != null)
            {
                var files = viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
                if (files.Any())
                {
                    // 先頭のコンテンツに切り替え
                    _ = SwitchToContent(files.First().FullPath, true);
                }
            }
        }

        // 末尾の画像に移動
        private void NavigateToLastImage()
        {
            // MainViewModelを使用
            var viewModel = MainViewModel;
            if (viewModel != null)
            {
                var files = viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
                if (files.Any())
                {
                    // 末尾のコンテンツに切り替え
                    _ = SwitchToContent(files.Last().FullPath, true);
                }
            }
        }

        /// <summary>
        /// 指定されたパスのコンテンツをロードします
        /// </summary>
        /// <param name="filePath">ファイルのパス</param>
        public void LoadContentFromPath(string filePath, bool notifyFileSelection = true)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            if (!File.Exists(filePath))
                return;

            if (_currentFilePath?.Equals(filePath, StringComparison.OrdinalIgnoreCase) ?? false)
            {
                // 同じファイルの場合は何もしない
                return;
            }

            _ = SwitchToContent(filePath, notifyFileSelection);
        }

        private void VideoPlayerControl_BackgroundDoubleClick(object sender, RoutedEventArgs e)
        {
            // VideoPlayerControlの背景がダブルクリックされたらウィンドウを閉じる
            Close();

        } // End of VideoPlayerControl_BackgroundDoubleClick

        private void WebpPlayer_BackgroundDoubleClick(object sender, RoutedEventArgs e)
        {
            // WebpPlayerControlの背景がダブルクリックされたらウィンドウを閉じる
            Close();
        }


        private void ImageViewerWindow_Activated(object? sender, EventArgs e)
        {
            // ウィンドウがアクティブになった時
            if (IsFullScreen)
            {
                // フルスクリーンモードであれば、カーソル非表示タイマーを開始（または再開）
                hideCursorTimer.Start();
            }
        }

        private void ImageViewerWindow_Deactivated(object? sender, EventArgs e)
        {
            // ウィンドウが非アクティブになった時
            // マウスカーソルを強制的に表示状態に戻す
            Mouse.OverrideCursor = Cursors.Arrow;
            // カーソル非表示タイマーを停止
            hideCursorTimer.Stop();
        }



        private void OnFileSelected(SelectedFileModel args)
        {
            LoadContentFromPath(args.FullPath, notifyFileSelection: false);
        }
    }
}
