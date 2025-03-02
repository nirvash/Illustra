using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.ComponentModel;
using Illustra.Helpers;
using Illustra.Models;

namespace Illustra.Views
{
    public partial class ImageViewerWindow : Window, INotifyPropertyChanged
    {
        // フルスクリーン切り替え前のウィンドウ状態を保存
        private WindowState _previousWindowState;
        private WindowStyle _previousWindowStyle;
        private bool _isFullScreen = false;

        private readonly DispatcherTimer _titleBarTimer;
        private const double TITLE_BAR_SHOW_AREA = 100; // マウスがここまで近づいたらタイトルバーを表示

        // 画像切り替え用
        private string _currentFilePath;
        public required ThumbnailListControl Parent { get; set; }

        private ImagePropertiesModel _properties = new();
        public ImagePropertiesModel Properties
        {
            get => _properties;
            private set
            {
                if (_properties != value)
                {
                    _properties = value;
                    OnPropertyChanged(nameof(Properties));
                }
            }
        }

        public string FileName { get; private set; }
        public BitmapSource? ImageSource { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ImageViewerWindow(string filePath)
        {
            InitializeComponent();
            FileName = System.IO.Path.GetFileName(filePath);
            _currentFilePath = filePath;

            LoadImageAndProperties(filePath);

            DataContext = this;

            // タイトルバー自動非表示用のタイマー
            _titleBarTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _titleBarTimer.Tick += (s, e) =>
            {
                _titleBarTimer.Stop();
                if (_isFullScreen)
                {
                    TitleBar.Visibility = Visibility.Collapsed;
                }
            };

            // ウィンドウの状態を復元
            var settings = ViewerSettingsHelper.LoadSettings();

            // 画面外に出ないように調整
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            // 位置が有効な値かつ画面内に収まるか確認
            if (!double.IsNaN(settings.Left) && settings.Left >= 0 && settings.Left + settings.Width <= screenWidth)
            {
                Left = settings.Left;
            }

            if (!double.IsNaN(settings.Top) && settings.Top >= 0 && settings.Top + settings.Height <= screenHeight)
            {
                Top = settings.Top;
            }

            // サイズも画面サイズを超えないように設定
            Width = Math.Min(settings.Width, screenWidth);
            Height = Math.Min(settings.Height, screenHeight);

            // フルスクリーン設定を保存して、Loaded後に適用
            _isFullScreen = settings.IsFullScreen;

            // プロパティパネルの表示状態を設定
            PropertyPanel.Visibility = settings.VisiblePropertyPanel ? Visibility.Visible : Visibility.Collapsed;
            PropertySplitter.Visibility = settings.VisiblePropertyPanel ? Visibility.Visible : Visibility.Collapsed;

            // プロパティパネル列の幅を設定
            if (settings.PropertyColumnWidth > 0)
            {
                // 保存されていた幅を読み込む
                _lastPropertyPanelWidth = settings.PropertyColumnWidth;

                // パネルが表示されていない場合は、幅を0に設定
                if (!settings.VisiblePropertyPanel)
                {
                    MainGrid.ColumnDefinitions[2].Width = new GridLength(0);
                }
                else
                {
                    MainGrid.ColumnDefinitions[2].Width = new GridLength(settings.PropertyColumnWidth);
                }
            }

            // ウィンドウが表示された後に実行する処理
            Loaded += (s, e) => OnWindowLoaded();
        }

        private async void LoadImageAndProperties(string filePath)
        {
            try
            {
                ImageSource = new BitmapImage(new Uri(filePath));
                var newProperties = await ImagePropertiesModel.LoadFromFileAsync(filePath);
                Properties = newProperties;

                // PropertyPanelControlにプロパティを設定
                if (PropertyPanelControl != null)
                {
                    PropertyPanelControl.ImageProperties = newProperties;

                    // DataContextを明示的に更新して確実に表示を更新
                    PropertyPanelControl.DataContext = newProperties;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image: {ex.Message}");
                MessageBox.Show($"画像の読み込みに失敗しました：{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnWindowLoaded()
        {
            // フルスクリーン状態を設定
            if (_isFullScreen)
            {
                _previousWindowState = WindowState;
                _previousWindowStyle = WindowStyle;

                // フルスクリーン状態にする
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;

                // 確実に設定が有効になるように少し待つ
                await Task.Delay(50);
            }

            // フォーカスを確実に設定
            await Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                Activate(); // ウィンドウをアクティブにする
                Focus();    // ウィンドウにフォーカスを設定
                MainImage.Focus(); // 画像にフォーカス
            }));
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                // クローズ処理を共通メソッドに委託
                CloseViewer();
            }
            else if (e.Key == Key.F11)
            {
                ToggleFullScreen();
            }
            else if (e.Key == Key.Left)
            {
                NavigateToPreviousImage();
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                NavigateToNextImage();
                e.Handled = true;
            }
            else if (e.Key == Key.P)
            {
                TogglePropertyPanel();
            }
        }

        private void TogglePropertyPanel()
        {
            if (PropertyPanel.Visibility == Visibility.Visible)
            {
                // プロパティパネルを非表示にする：幅を保存してから0に設定
                _lastPropertyPanelWidth = MainGrid.ColumnDefinitions[2].ActualWidth;
                MainGrid.ColumnDefinitions[2].Width = new GridLength(0);
                PropertyPanel.Visibility = Visibility.Collapsed;
                PropertySplitter.Visibility = Visibility.Collapsed;
            }
            else
            {
                // プロパティパネルを表示する：保存していた幅を復元
                double width = _lastPropertyPanelWidth > 0 ? _lastPropertyPanelWidth : 250;
                MainGrid.ColumnDefinitions[2].Width = new GridLength(width);
                PropertyPanel.Visibility = Visibility.Visible;
                PropertySplitter.Visibility = Visibility.Visible;
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
                LoadNewImage(previousFilePath);
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
                LoadNewImage(nextFilePath);
            }
        }

        private const int CacheSize = 10; // キャッシュサイズを固定
        private readonly Dictionary<string, BitmapSource> _imageCache = new();
        private readonly Queue<string> _cacheOrder = new(); // キャッシュの順序を管理

        private void CacheImages(string filePath)
        {
            var _viewModel = Parent.GetViewModel();
            var fileNodes = _viewModel.Items;
            var currentIndex = fileNodes.ToList().FindIndex(f => f.FullPath == filePath);

            // 前後の画像をプリロード
            for (int i = -CacheSize; i <= CacheSize; i++)
            {
                var index = currentIndex + i;
                if (index >= 0 && index < fileNodes.Count)
                {
                    var targetPath = fileNodes[index].FullPath;
                    if (!_imageCache.ContainsKey(targetPath))
                    {
                        try
                        {
                            var image = new BitmapImage();
                            image.BeginInit();
                            image.CacheOption = BitmapCacheOption.OnLoad; // メモリ効率を改善
                            image.UriSource = new Uri(targetPath);
                            image.EndInit();
                            image.Freeze(); // UIスレッドの効率を改善

                            _imageCache[targetPath] = image;
                            _cacheOrder.Enqueue(targetPath);

                            // キャッシュサイズを超えた場合、古いものから削除
                            while (_cacheOrder.Count > CacheSize * 2 + 1)
                            {
                                var oldestPath = _cacheOrder.Dequeue();
                                _imageCache.Remove(oldestPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"画像のキャッシュ中にエラーが発生: {ex.Message}");
                        }
                    }
                }
            }
        }

        private void ClearCache()
        {
            _imageCache.Clear();
            _cacheOrder.Clear();
        }

        private void LoadImageFromCache(string filePath)
        {
            try
            {
                if (_imageCache.TryGetValue(filePath, out var cachedImage))
                {
                    ImageSource = cachedImage;
                }
                else
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(filePath);
                    image.EndInit();
                    image.Freeze();
                    ImageSource = image;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"画像の読み込み中にエラーが発生: {ex.Message}");
                throw;
            }
        }

        // 新しい画像を読み込む
        private void LoadNewImage(string filePath)
        {
            try
            {
                // 画像を読み込み
                _currentFilePath = filePath;
                FileName = System.IO.Path.GetFileName(filePath);
                LoadImageFromCache(filePath);
                CacheImages(filePath);

                // データバインディングを更新
                Title = FileName;
                MainImage.Source = ImageSource;

                // 親ウィンドウのサムネイル選択を更新
                Parent.SyncThumbnailSelection(filePath);
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

        /// <summary>
        /// ビューアを閉じる共通処理
        /// </summary>
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

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isFullScreen) return;

            var position = e.GetPosition(this);
            if (position.Y <= TITLE_BAR_SHOW_AREA)
            {
                TitleBar.Visibility = Visibility.Visible;
                _titleBarTimer.Stop();
                _titleBarTimer.Start();
            }
            else
            {
                TitleBar.Visibility = Visibility.Collapsed;
                _titleBarTimer.Stop();
            }
        }

        private void ToggleFullScreen()
        {
            if (!_isFullScreen)
            {
                // 現在の状態を保存
                _previousWindowState = WindowState;
                _previousWindowStyle = WindowStyle;

                // フルスクリーンに切り替え
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                _isFullScreen = true;

                // 状態変更をすぐに保存
                SaveCurrentSettings();
            }
            else
            {
                // 保存していた状態に戻す
                WindowStyle = _previousWindowStyle;
                WindowState = _previousWindowState;
                _isFullScreen = false;

                // タイトルバーを非表示
                TitleBar.Visibility = Visibility.Collapsed;
                _titleBarTimer.Stop();

                // 状態変更をすぐに保存
                SaveCurrentSettings();
            }
        }

        // 現在のウィンドウ設定を保存する共通メソッド
        private void SaveCurrentSettings()
        {
            var settings = new ViewerSettings
            {
                Left = _isFullScreen ? double.NaN : Left,
                Top = _isFullScreen ? double.NaN : Top,
                Width = _isFullScreen ? 800 : Width,
                Height = _isFullScreen ? 600 : Height,
                IsFullScreen = _isFullScreen,
                VisiblePropertyPanel = PropertyPanel.Visibility == Visibility.Visible,
                // パネルが非表示の場合は保存されている幅を使用、表示されている場合は現在の幅を保存
                PropertyColumnWidth = PropertyPanel.Visibility == Visibility.Visible
                    ? MainGrid.ColumnDefinitions[2].ActualWidth
                    : _lastPropertyPanelWidth
            };
            ViewerSettingsHelper.SaveSettings(settings);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 閉じる過程での最初の段階でフルスクリーン状態を保存
            // 共通メソッドを使用して設定を保存
            SaveCurrentSettings();

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // OnClosingで既に保存したので、ここでは何もしない
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
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
    }
}
