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
using System.Threading;
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
            _displayedFilePath = null;
            WebpPlayer.Visibility = Visibility.Visible;
            await WebpPlayer.LoadWebpAsync(filePath);
            _displayedFilePath = filePath;
        }

        private const string CONTROL_ID = "ImageViewer";
        // フルスクリーン切り替え前のウィンドウ状態を保存
        private bool _isFullScreen = false;
        private bool _returnToInlineAfterFullscreenExit;

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
                    if (!value && _returnToInlineAfterFullscreenExit)
                    {
                        _returnToInlineAfterFullscreenExit = false;
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                            Parent?.ReturnInlineAfterFullscreen(this, RequestedFilePath)));
                    }
                }
            }
        }


        // 画像切り替え用
        private string _currentFilePath;
        private string? _displayedFilePath;
        public string? DisplayedFilePath => _displayedFilePath;
        public string? RequestedFilePath => ViewerHostLogic.ResolveCurrentPath(_currentFilePath, _displayedFilePath);
        public void FocusInlineSurface()
        {
            if (_isInlineHosted) ViewerHostLogic.FocusDetachedSurface(ViewerSurface);
            else Focus();
        }

        public void ToggleFullScreenFromHost()
        {
            _returnToInlineAfterFullscreenExit = true;
            ToggleFullScreen();
        }

        public void BeginMediaHostTransfer()
        {
            WebpPlayer.BeginHostTransfer();
            VideoPlayerControl.BeginHostTransfer();
        }

        public void CancelReturnToInlineAfterFullscreen() => _returnToInlineAfterFullscreenExit = false;

        private void DockInline_Click(object sender, RoutedEventArgs e)
        {
            if (Parent != null) Parent.DockSeparateViewerToInline(this);
        }

        public FrameworkElement DetachSurfaceForInlineHost()
        {
            if (Content is not FrameworkElement surface)
                throw new InvalidOperationException("Viewer surface is unavailable.");
            _inlinePropertyPanelVisibility = PropertyPanel.Visibility;
            _inlinePropertySplitterVisibility = PropertySplitter.Visibility;
            _inlineSplitterWidth = MainGrid.ColumnDefinitions[1].Width;
            _inlinePropertyWidth = MainGrid.ColumnDefinitions[2].Width;
            ViewerHostLogic.PrepareDetachedSurface(surface, this);
            surface.PreviewKeyDown += Window_PreviewKeyDown;
            surface.KeyDown += Window_KeyDown;
            surface.PreviewMouseDown += Window_PreviewMouseDown;
            ImageZoomControl.MouseLeftButtonDown += InlineMedia_MouseLeftButtonDown;
            WebpPlayer.MouseLeftButtonDown += InlineMedia_MouseLeftButtonDown;
            VideoPlayerControl.MouseLeftButtonDown += InlineMedia_MouseLeftButtonDown;
            PropertyPanel.Visibility = Visibility.Collapsed;
            PropertySplitter.Visibility = Visibility.Collapsed;
            MainGrid.ColumnDefinitions[1].Width = new GridLength(0);
            MainGrid.ColumnDefinitions[2].Width = new GridLength(0);
            _appContext?.SetViewerPropertyPanelVisible(false);
            _isInlineHosted = true;
            WebpPlayer.BeginHostTransfer();
            VideoPlayerControl.BeginHostTransfer();
            Content = null;
            return surface;
        }

        public void AttachSurfaceToWindowHost()
        {
            if (Content is not FrameworkElement surface)
                throw new InvalidOperationException("Viewer surface is unavailable.");
            surface.PreviewKeyDown -= Window_PreviewKeyDown;
            surface.KeyDown -= Window_KeyDown;
            surface.PreviewMouseDown -= Window_PreviewMouseDown;
            ImageZoomControl.MouseLeftButtonDown -= InlineMedia_MouseLeftButtonDown;
            WebpPlayer.MouseLeftButtonDown -= InlineMedia_MouseLeftButtonDown;
            VideoPlayerControl.MouseLeftButtonDown -= InlineMedia_MouseLeftButtonDown;
            RestoreInlinePropertyPanel();
            _isInlineHosted = false;
        }

        public void PrepareTemporaryFullscreenHost(Window owner)
        {
            _returnToInlineAfterFullscreenExit = true;
            IsTemporaryFullscreenHost = true;
            SaveWindowPosition = ViewerHostLogic.ShouldPersistWindowPlacement(IsTemporaryFullscreenHost);
            WindowStartupLocation = WindowStartupLocation.Manual;
            var ownerBounds = owner.WindowState == WindowState.Normal
                            ? new Rect(owner.Left, owner.Top, owner.Width, owner.Height)
                            : owner.RestoreBounds;
            Left = ownerBounds.Left;
            Top = ownerBounds.Top;
            base.ShowTitleBar = false;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            IsFullScreen = true;
            UpdateControlsVisibility();
        }

        private void RestoreInlinePropertyPanel()
        {
            if (!_inlinePropertyPanelVisibility.HasValue) return;
            PropertyPanel.Visibility = _inlinePropertyPanelVisibility.Value;
            PropertySplitter.Visibility = _inlinePropertySplitterVisibility ?? Visibility.Collapsed;
            ViewerHostLogic.ApplyPropertyPanelLayout(MainGrid, PropertyPanel.Visibility == Visibility.Visible, _inlinePropertyWidth?.Value ?? 0);
            _inlinePropertyPanelVisibility = null;
            _inlinePropertySplitterVisibility = null;
            _inlineSplitterWidth = null;
            _inlinePropertyWidth = null;
            _appContext?.SetViewerPropertyPanelVisible(PropertyPanel.Visibility == Visibility.Visible);
        }

        public void DisposeInlineSurface()
        {
            if (_isClosing) return;
            CleanupSafely(() => CancelAndDispose(ref _imageLoadCancellationTokenSource));
            CleanupSafely(() => CancelAndDispose(ref _preloadCancellationTokenSource));
            CleanupSafely(() => _slideshowTimer.Stop());
            CleanupSafely(() => hideCursorTimer.Stop());
            CleanupSafely(() => WebpPlayer.Stop());
            CleanupSafely(() => VideoPlayerControl.StopVideo());
            CleanupSafely(() => ContainerLocator.Container.Resolve<IEventAggregator>()
                ?.GetEvent<FileSelectedEvent>()?.Unsubscribe(OnFileSelected));
            CleanupSafely(Close);
        }
        private bool _isInlineHosted;
        private Visibility? _inlinePropertyPanelVisibility;
        private Visibility? _inlinePropertySplitterVisibility;
        private GridLength? _inlineSplitterWidth;
        private GridLength? _inlinePropertyWidth;
        public bool IsTemporaryFullscreenHost { get; set; }
        private bool _isClosing;
        private CancellationTokenSource? _imageLoadCancellationTokenSource;
        private CancellationTokenSource? _preloadCancellationTokenSource;
        private bool _isSlideshowActive = false;
        private readonly DispatcherTimer _slideshowTimer;
        public new ThumbnailListControl? Parent { get; set; }
        private DatabaseManager? _dbManager;

        private IllustraAppContext? _appContext;
        private PropertyChangedEventHandler? _appContextPropertyChangedHandler;
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
            KeyDown += Window_KeyDown;
            PreviewKeyDown += Window_PreviewKeyDown;
            MouseDoubleClick += Window_MouseDoubleClick;

            // キャッシュの初期化
            _imageCache = new WindowBasedImageCache();
            _dbManager = ContainerLocator.Container.Resolve<DatabaseManager>();
            _appContext = ContainerLocator.Container.Resolve<IllustraAppContext>();

            _appContextPropertyChangedHandler = (_, e) =>
            {
                if (e.PropertyName == nameof(_appContext.CurrentProperties))
                {
                    Properties = _appContext.CurrentProperties;
                    OnPropertyChanged(nameof(Properties));
                }
            };
            _appContext.PropertyChanged += _appContextPropertyChangedHandler;
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
            ImageZoomControl.PreviewMouseRightButtonDown += Media_MouseRightButtonDown;
            WebpPlayer.PreviewMouseRightButtonDown += Media_MouseRightButtonDown;
            VideoPlayerControl.PreviewMouseRightButtonDown += Media_MouseRightButtonDown;

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

                if (WebpPlayer.Visibility == Visibility.Visible && WebpPlayer.IsMouseOverControls)
                {
                    // WebpPlayerControl が表示されていて、かつそのコントロール上にマウスがある場合は隠さない
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

            // 表示状態を共有コンテキストへ通知（非表示ならメタデータ解析をスキップする）
            _appContext.SetViewerPropertyPanelVisible(settings.VisiblePropertyPanel);

            // フルスクリーン状態に応じた幅を読み込む
            _lastPropertyPanelWidth = settings.IsFullScreen
                ? settings.FullScreenPropertyColumnWidth
                : settings.NormalPropertyColumnWidth;

            // Actual panel visibility is authoritative; saved visibility can belong to another host.
            ViewerHostLogic.ApplyPropertyPanelLayout(MainGrid, PropertyPanel.Visibility == Visibility.Visible, _lastPropertyPanelWidth);
            Activated += ImageViewerWindow_Activated;
            Deactivated += ImageViewerWindow_Deactivated;


            // ウィンドウが表示された後に実行する処理
            Loaded += (s, e) => OnWindowLoaded();
            // A viewer surface can move between hosts; unloading is not disposal.
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
                if (!_isClosing) return;
                CancelAndDispose(ref _imageLoadCancellationTokenSource);
                CancelAndDispose(ref _preloadCancellationTokenSource);

                // イベントの購読解除
                var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                eventAggregator?.GetEvent<FileSelectedEvent>()?.Unsubscribe(OnFileSelected);
            };
        }

        private void Media_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var targetPath = _displayedFilePath;
            if (string.IsNullOrEmpty(targetPath) || sender is not FrameworkElement placementTarget)
                return;

            e.Handled = true;
            ShowPromptMenu(targetPath, placementTarget);
        }

        private void ShowPromptMenu(string targetPath, FrameworkElement placementTarget)
        {
            // コンテキストメニューを作成
            var menu = new ContextMenu();
            var targetProperties = _appContext?.CurrentProperties;
            if (!string.Equals(targetProperties?.FilePath, targetPath, StringComparison.OrdinalIgnoreCase))
                targetProperties = null;

            if (targetProperties?.StableDiffusionResult != null)
            {
                var copyPromptItem = new MenuItem
                {
                    Header = (string)Application.Current.FindResource("String_Thumbnail_CopyPrompt")
                };
                copyPromptItem.Click += (s, e) => CopyPrompt(PromptCopyType.Positive, targetProperties);
                menu.Items.Add(copyPromptItem);

                if (!string.IsNullOrEmpty(targetProperties.StableDiffusionResult.NegativePrompt))
                {
                    var copyNegativePromptItem = new MenuItem
                    {
                        Header = (string)Application.Current.FindResource("String_Thumbnail_CopyNegativePrompt")
                    };
                    copyNegativePromptItem.Click += (s, e) => CopyPrompt(PromptCopyType.Negative, targetProperties);
                    menu.Items.Add(copyNegativePromptItem);
                }

                var copyAllPromptItem = new MenuItem
                {
                    Header = (string)Application.Current.FindResource("String_Thumbnail_CopyAllPrompt")
                };
                copyAllPromptItem.Click += (s, e) => CopyPrompt(PromptCopyType.All, targetProperties);
                menu.Items.Add(copyAllPromptItem);
                menu.Items.Add(new Separator());
            }

            var copyPathItem = new MenuItem
            {
                Header = (string)Application.Current.FindResource("String_Thumbnail_CopyFilePath")
            };
            copyPathItem.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(targetPath);
                    ToastNotificationHelper.ShowRelativeTo(GetToastOwner(), (string)Application.Current.FindResource("String_Thumbnail_FilePathCopied"));
                }
                catch (Exception ex) { Debug.WriteLine($"パスのコピーに失敗しました: {ex.Message}"); }
            };
            menu.Items.Add(copyPathItem);

            var copyImageItem = new MenuItem
            {
                Header = (string)Application.Current.FindResource("String_Thumbnail_CopyImage"),
                IsEnabled = FileHelper.IsImageFile(targetPath)
            };
            copyImageItem.Click += (s, e) =>
            {
                try
                {
                    ImageClipboardHelper.CopyImageToClipboard(targetPath);
                    ToastNotificationHelper.ShowRelativeTo(GetToastOwner(), (string)Application.Current.FindResource("String_Thumbnail_ImageCopied"));
                }
                catch (Exception ex) { Debug.WriteLine($"画像のコピーに失敗しました: {ex.Message}"); }
            };
            menu.Items.Add(copyImageItem);

            var copyFileItem = new MenuItem
            {
                Header = (string)Application.Current.FindResource("String_Thumbnail_CopyFile")
            };
            copyFileItem.Click += (s, e) =>
            {
                try
                {
                    var data = new DataObject();
                    data.SetData(DataFormats.FileDrop, new[] { targetPath });
                    Clipboard.SetDataObject(data, true);
                }
                catch (Exception ex) { Debug.WriteLine($"ファイルのコピーに失敗しました: {ex.Message}"); }
            };
            menu.Items.Add(copyFileItem);

            // メニューを表示
            menu.PlacementTarget = placementTarget;
            placementTarget.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private enum PromptCopyType { Positive, Negative, All }

        private FrameworkElement GetToastOwner() => _isInlineHosted && Parent != null
            ? Parent
            : this;

        private void CopyPrompt(PromptCopyType type, ImagePropertiesModel properties)
        {
            if (properties.StableDiffusionResult == null) return;

            try
            {
                string textToCopy = "";
                var result = properties.StableDiffusionResult;

                switch (type)
                {
                    case PromptCopyType.Positive:
                        textToCopy = result.Prompt;
                        break;
                    case PromptCopyType.Negative:
                        textToCopy = result.NegativePrompt;
                        break;
                    case PromptCopyType.All:
                        textToCopy = properties.UserComment; // UserComment全体をコピー
                        break;
                }

                if (!string.IsNullOrEmpty(textToCopy))
                {
                    Clipboard.SetText(textToCopy.Trim());
                    ToastNotificationHelper.ShowRelativeTo(GetToastOwner(), (string)Application.Current.FindResource("String_Thumbnail_PromptCopied"));
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
                    if (_isInlineHosted) ViewerHostLogic.FocusDetachedSurface(ViewerSurface); else this.Focus();
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
            if (Parent != null && !Parent.IsCurrentViewerOwner()) return;
            var shortcutHandler = KeyboardShortcutHandler.Instance;

            if (e.Key == Key.Tab)
            {
                e.Handled = true; // `Tab` キーでのフォーカス移動を抑止
                return;
            }

            // 各機能のショートカットをチェック
            if (shortcutHandler.IsShortcutMatch(FuncId.CloseViewer, e.Key))
            {
                RequestCloseViewer();
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
            if (Parent != null && !Parent.IsCurrentViewerOwner()) return;
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
            if (_isInlineHosted)
            {
                Parent?.ToggleMainPropertyPanel();
                return;
            }

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
            // 一覧内からの全画面ではウィンドウ設定を保存せず、明示的なパネル切替だけ記憶する。
            if (IsTemporaryFullscreenHost && !_isInlineHosted)
            {
                var settings = ViewerSettingsHelper.LoadSettings();
                settings.VisiblePropertyPanel = PropertyPanel.Visibility == Visibility.Visible;
                ViewerSettingsHelper.SaveSettings(settings);
            }


            // 表示状態の変更を共有コンテキストへ通知（表示時は現在表示中ファイルのプロパティを再読み込み）
            _appContext.SetViewerPropertyPanelVisible(PropertyPanel.Visibility == System.Windows.Visibility.Visible);
        }

        private void TogglePropertyPanel_Click(object sender, RoutedEventArgs e)
        {
            TogglePropertyPanel();
        }

        private void PropertyPanelButton_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Viewer全体のダブルクリック処理（画像クリックで閉じる）へ伝播させない。
            e.Handled = true;
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
            if (Parent != null && !Parent.IsCurrentViewerOwner()) return;
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
            if (Parent != null && !Parent.IsCurrentViewerOwner()) return;
            if (Parent == null) return;

            // 親ウィンドウに次の画像への移動をリクエスト
            string? nextFilePath = Parent.GetNextImage(_currentFilePath);
            if (!string.IsNullOrEmpty(nextFilePath))
            {
                _ = SwitchToContent(nextFilePath, true); // Use SwitchToContent
            }
            else if (_isSlideshowActive)
            {
                // フォルダー監視中は新しい画像が追加される可能性があるため、
                // 次の画像が一覧に現れるまでタイマーを動かしたまま待つ。
                if (Parent.IsWatchingCurrentFolder)
                    return;

                // 次の画像がなく、監視もしていない場合はスライドショーを停止
                _slideshowTimer.Stop();
                _isSlideshowActive = false;
                ShowNotification(iconKind: PackIconMaterialDesignKind.Pause); // アイコン表示に変更
            }
        }

        // Renamed from LoadAndDisplayImage
        private async Task LoadAndDisplayContent(string filePath, CancellationToken cancellationToken)
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

                var isWebP = string.Equals(Path.GetExtension(filePath), ".webp", StringComparison.OrdinalIgnoreCase);
                var formatCheckTiming = ViewerPerformanceLog.IsEnabled ? Stopwatch.StartNew() : null;
                var isAnimatedWebP = isWebP && await WebPHelper.IsAnimatedWebPAsync(filePath);
                if (formatCheckTiming != null)
                    ViewerPerformanceLog.Append($"format-check path=\"{filePath}\" elapsedMs={formatCheckTiming.Elapsed.TotalMilliseconds:F3} animated={isAnimatedWebP} skipped={(isWebP ? "none" : "non-webp")}");
                if (isAnimatedWebP)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _displayedFilePath = null;
                    WebpPlayer.Visibility = Visibility.Visible;
                    LogHelper.LogWithTimestamp("LoadAndDisplayContent - Before LoadWebpAsync", LogHelper.Categories.Performance);
                    await WebpPlayer.LoadWebpAsync(filePath);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                        return;
                    ImageZoomControl.Visibility = Visibility.Collapsed;
                    _displayedFilePath = filePath;
                    LogHelper.LogWithTimestamp("LoadAndDisplayContent - After LoadWebpAsync", LogHelper.Categories.Performance);
                }
                else
                {
                    await ShowStaticImageAsync(filePath, cancellationToken);
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

            if (string.Equals(Path.GetExtension(filePath), ".webp", StringComparison.OrdinalIgnoreCase)
                && await WebPHelper.IsAnimatedWebPAsync(filePath))
            {
                WebpPlayer.Visibility = Visibility.Visible;
                _displayedFilePath = null;
                await WebpPlayer.LoadWebpAsync(filePath);
                ImageZoomControl.Visibility = Visibility.Collapsed;
                _displayedFilePath = filePath;
            }
            else
            {
                await ShowStaticImageAsync(filePath, CancellationToken.None);
            }
        }

        private void ShowVideo(string filePath)
        {
            _displayedFilePath = null;
            // Hide other controls
            ImageZoomControl.Visibility = Visibility.Collapsed;
            WebpPlayer.Visibility = Visibility.Collapsed;

            // Show video player and set source
            VideoPlayerControl.Visibility = Visibility.Visible;
            VideoPlayerControl.FilePath = filePath; // Set FilePath to trigger loading in the control
            _displayedFilePath = filePath;
        }

        private async Task ShowStaticImageAsync(string filePath, CancellationToken cancellationToken)
        {
            if (ImageZoomControl.Visibility != Visibility.Visible)
                _displayedFilePath = null;
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
                var imageTiming = ViewerPerformanceLog.IsEnabled ? Stopwatch.StartNew() : null;
                var image = await _imageCache.GetImageAsync(filePath, cancellationToken);
                var cacheMs = imageTiming?.Elapsed.TotalMilliseconds ?? 0;
                cancellationToken.ThrowIfCancellationRequested();

                if (string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    ImageSource = image;
                    _displayedFilePath = filePath;
                    if (imageTiming != null) ViewerPerformanceLog.Append($"image-assign path=\"{filePath}\" cacheMs={cacheMs:F3} assignMs={imageTiming.Elapsed.TotalMilliseconds - cacheMs:F3}");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 画像切替によるキャンセルは正常な動作として扱う。
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
            if (Parent != null && !Parent.IsCurrentViewerOwner()) return;
            LogHelper.LogWithTimestamp("SwitchToContent - Start", LogHelper.Categories.Performance);
            var measurePerformance = ViewerPerformanceLog.IsEnabled;
            var stopwatch = measurePerformance ? Stopwatch.StartNew() : null;
            using var performanceRequest = measurePerformance ? ViewerPerformanceLog.BeginRequest(filePath) : null;
            double loadEndMs = 0;
            double renderEndMs = 0;
            double notifyMs = 0;
            try
            {
                if (_currentFilePath?.Equals(filePath, StringComparison.OrdinalIgnoreCase) ?? false)
                {
                    // 同じファイルの場合は何もしない
                    return;
                }

                // Stop video and webp animation if playing before switching content
                if (VideoPlayerControl.Visibility == Visibility.Visible)
                {
                    VideoPlayerControl.StopVideo();
                }

                // Stop WebP animation if visible
                if (WebpPlayer.Visibility == Visibility.Visible)
                {
                    WebpPlayer.Stop();
                    WebpPlayer.Visibility = Visibility.Collapsed;
                }

                hideCursorTimer.Start();

                // 1. 現在のファイルパスを更新
                _currentFilePath = filePath;
                CancelAndDispose(ref _imageLoadCancellationTokenSource);
                _imageLoadCancellationTokenSource = new CancellationTokenSource();
                var imageLoadCancellationToken = _imageLoadCancellationTokenSource.Token;
                var cacheHitBeforeLoad = FileHelper.IsImageFile(filePath) && _imageCache.HasImage(filePath);

                // 2. コンテンツを表示
                LogHelper.LogWithTimestamp("SwitchToContent - Before LoadAndDisplayContent", LogHelper.Categories.Performance);
                var loadStartMs = stopwatch?.Elapsed.TotalMilliseconds ?? 0;
                await LoadAndDisplayContent(filePath, imageLoadCancellationToken); // Call LoadAndDisplayContent
                loadEndMs = stopwatch?.Elapsed.TotalMilliseconds ?? 0;
                if (measurePerformance) ViewerPerformanceLog.Append($"switch-load path=\"{filePath}\" prepareMs={loadStartMs:F3} loadMs={loadEndMs - loadStartMs:F3} cancelled={imageLoadCancellationToken.IsCancellationRequested}");

                if (imageLoadCancellationToken.IsCancellationRequested ||
                    !string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                LogHelper.LogWithTimestamp("SwitchToContent - After LoadAndDisplayContent", LogHelper.Categories.Performance);
                if (measurePerformance && ImageZoomControl.Visibility == Visibility.Visible)
                {
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    renderEndMs = stopwatch!.Elapsed.TotalMilliseconds;
                    ViewerPerformanceLog.Append($"static-switch path=\"{filePath}\" renderMs={stopwatch.ElapsedMilliseconds} cacheHit={cacheHitBeforeLoad} renderQueueMs={renderEndMs - loadEndMs:F3}");
                }
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
                        CancelAndDispose(ref _preloadCancellationTokenSource);
                        _preloadCancellationTokenSource = new CancellationTokenSource();
                        _ = PreloadImagesAsync(files, currentIndex, _preloadCancellationTokenSource.Token);
                    }
                }

                // 親ウィンドウのサムネイル選択を更新
                if (notifyFileSelection)
                {
                    var notifyStartMs = stopwatch?.Elapsed.TotalMilliseconds ?? 0;
                    var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                    eventAggregator?.GetEvent<FileSelectedEvent>()?.Publish(
                        new SelectedFileModel(CONTROL_ID, filePath));
                    notifyMs = (stopwatch?.Elapsed.TotalMilliseconds ?? 0) - notifyStartMs;
                }
            }
            catch (OperationCanceledException)
            {
                // 画像切替によるキャンセルは正常な動作として扱う。
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading content: {ex.Message}");
                MessageBox.Show($"コンテンツの読み込みに失敗しました：{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (measurePerformance) ViewerPerformanceLog.Append($"switch-end path=\"{filePath}\" totalMs={stopwatch!.Elapsed.TotalMilliseconds:F3} postMs={stopwatch.Elapsed.TotalMilliseconds - Math.Max(loadEndMs, renderEndMs):F3} notifyMs={notifyMs:F3}");
            }
        }

        private async Task PreloadImagesAsync(List<FileNodeModel> files, int currentIndex, CancellationToken cancellationToken)
        {
            try
            {
                await _imageCache.UpdateCacheAsync(files, currentIndex, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 新しい表示位置への切替によるキャンセルは正常な動作として扱う。
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"画像プリロード中にエラーが発生: {ex.Message}", ex);
            }
        }

        private static void CancelAndDispose(ref CancellationTokenSource? cancellationTokenSource)
        {
            var source = cancellationTokenSource;
            cancellationTokenSource = null;
            if (source == null) return;
            try { source.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (AggregateException ex) { Debug.WriteLine($"Cancellation callback failed: {ex.Message}"); }
            finally { source.Dispose(); }
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // ウィンドウコマンド（プロパティパネル等）のダブルクリックを
            // ビューア全体の「閉じる」ジェスチャとして扱わない。
            var clickedElement = e.OriginalSource as DependencyObject;
            if (clickedElement != null &&
                (IsDescendantOf(clickedElement, WindowCommands) ||
                 IsDescendantOf(clickedElement, FullScreenControls)))
            {
                e.Handled = true;
                return;
            }

            // VideoPlayerまたはWebpPlayerが表示されている場合は、各コントロール側のイベントで処理するため何もしない
            if (VideoPlayerControl.Visibility == Visibility.Visible || WebpPlayer.Visibility == Visibility.Visible)
            {
                return;
            }

            // VideoPlayerが表示されていない場合（画像表示など）はここで処理
            RequestCloseViewer();
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

            // Actual panel visibility is authoritative; saved visibility can belong to another host.
            ViewerHostLogic.ApplyPropertyPanelLayout(MainGrid, PropertyPanel.Visibility == Visibility.Visible, _lastPropertyPanelWidth);
        }

        // フルスクリーン切り替えボタンのクリックイベント
        private void ToggleFullScreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        // キーショートカットからのフルスクリーン切り替え
        private void ToggleFullScreen()
        {
            if (Parent != null && !Parent.IsCurrentViewerOwner()) return;
            if (_isInlineHosted)
            {
                Parent?.OpenSeparateViewerForInlineFullscreen(RequestedFilePath);
                return;
            }

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
            if (!ViewerHostLogic.ShouldPersistWindowSettings(_isInlineHosted, IsTemporaryFullscreenHost)) return;
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
            if (_isClosing)
            {
                base.OnClosing(e);
                return;
            }
            _isClosing = true;
            _returnToInlineAfterFullscreenExit = false;
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
                if (IsTemporaryFullscreenHost)
                    SaveWindowPosition = ViewerHostLogic.ShouldPersistWindowPlacement(IsTemporaryFullscreenHost);

                // タイマーをキャンセルしてマウスカーソルを表示状態に戻す
                Mouse.OverrideCursor = Cursors.Arrow;
                hideCursorTimer.Stop();
                CancelAndDispose(ref _imageLoadCancellationTokenSource);
                CancelAndDispose(ref _preloadCancellationTokenSource);
                WebpPlayer.Stop();
                VideoPlayerControl.StopVideo();
                WebpPlayer.DisposeForFinalClose();
                VideoPlayerControl.DisposeForFinalClose();

                // 画像リソースの解放
                ImageSource = null;
                _displayedFilePath = null;

                // キャッシュをクリア
                _imageCache.Clear();

                var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                eventAggregator?.GetEvent<FileSelectedEvent>()?.Unsubscribe(OnFileSelected);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Closing error: {ex.Message}");
            }
            finally
            {
                CleanupSafely(() => CancelAndDispose(ref _imageLoadCancellationTokenSource));
                CleanupSafely(() => CancelAndDispose(ref _preloadCancellationTokenSource));
                CleanupSafely(() => _slideshowTimer.Stop());
                CleanupSafely(() => hideCursorTimer.Stop());
                CleanupSafely(() => WebpPlayer.DisposeForFinalClose());
                CleanupSafely(() => VideoPlayerControl.DisposeForFinalClose());
                CleanupSafely(() => _imageCache.Clear());
                CleanupSafely(() => ContainerLocator.Container.Resolve<IEventAggregator>()
                    ?.GetEvent<FileSelectedEvent>()?.Unsubscribe(OnFileSelected));
            }

            base.OnClosing(e);
        }

        private static void CleanupSafely(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception ex) { Debug.WriteLine($"Viewer cleanup failed: {ex.Message}"); }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // OnClosingで既に保存したので、ここでは何もしない

            // Inline surface must not hide the main window's property panel.
            if (!_isInlineHosted)
                _appContext?.SetViewerPropertyPanelVisible(false);
            if (_appContext != null && _appContextPropertyChangedHandler != null)
                _appContext.PropertyChanged -= _appContextPropertyChangedHandler;

            // サムネイルリストにフォーカスを設定
            if (_isInlineHosted) Parent?.OnInlineViewerClosed(this);
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
            if (_isInlineHosted) ViewerHostLogic.FocusDetachedSurface(ViewerSurface); else this.Focus();
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
            if (Parent != null && !Parent.IsCurrentViewerOwner()) return;
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
                ToastNotificationHelper.ShowRelativeTo(GetToastOwner(), message);

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
                    RequestCloseViewer();
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
            RequestCloseViewer();

        } // End of VideoPlayerControl_BackgroundDoubleClick

        private void WebpPlayer_BackgroundDoubleClick(object sender, RoutedEventArgs e)
        {
            // WebpPlayerControlの背景がダブルクリックされたらウィンドウを閉じる
            RequestCloseViewer();
        }

        private void RequestCloseViewer()
        {
            if (_isInlineHosted)
                Parent?.CloseInlineViewer();
            else
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
            if (Parent == null || !Parent.IsCurrentViewerOwner())
                return;
            LoadContentFromPath(args.FullPath, notifyFileSelection: false);
        }

        private void InlineMedia_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isInlineHosted || e.Handled ||
                !ViewerHostLogic.IsDoubleClick(e.ClickCount, e.ChangedButton, sender is Control))
                return;

            RequestCloseViewer();
            e.Handled = true;
        }
    }
}
