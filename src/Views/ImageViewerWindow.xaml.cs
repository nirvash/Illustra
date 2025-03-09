using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.ComponentModel;
using System.Windows.Media;
using Prism.Events;
using Prism.Ioc;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.Events;
using System.Windows.Media.Animation;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Illustra.Helpers.Interfaces;

namespace Illustra.Views
{
    public partial class ImageViewerWindow : Window, INotifyPropertyChanged
    {
        // フルスクリーン切り替え前のウィンドウ状態を保存
        private WindowState _previousWindowState;
        private WindowStyle _previousWindowStyle;
        private bool _isFullScreen = false;

        public bool IsFullScreen
        {
            get => _isFullScreen;
            private set
            {
                if (_isFullScreen != value)
                {
                    _isFullScreen = value;
                    OnPropertyChanged(nameof(IsFullScreen));
                }
            }
        }

        private readonly DispatcherTimer _titleBarTimer;
        private const double TITLE_BAR_SHOW_AREA = 100; // マウスがここまで近づいたらタイトルバーを表示

        // 画像切り替え用
        private string _currentFilePath;
        public new ThumbnailListControl? Parent { get; set; }

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

                    // 明示的に変更を通知
                    OnPropertyChanged(nameof(Properties));

                    // Ratingプロパティの変更を監視するためのハンドラーを設定
                    if (oldProperties != null)
                    {
                        oldProperties.PropertyChanged -= Properties_PropertyChanged;
                    }
                    if (_properties != null)
                    {
                        _properties.PropertyChanged += Properties_PropertyChanged;
                    }
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

            // キャッシュから画像を読み込み（なければ新規作成）
            LoadImageFromCache(filePath);

            // プロパティの読み込み
            LoadImagePropertiesAsync(filePath);

            // イベントの購読
            var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            eventAggregator?.GetEvent<RatingChangedEvent>()?.Subscribe(OnRatingChanged);

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

            // マウスカーソル非表示用のタイマー
            hideCursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            hideCursorTimer.Tick += (s, args) =>
            {
                Mouse.OverrideCursor = Cursors.None;
                hideCursorTimer.Stop();
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
            IsFullScreen = settings.IsFullScreen; // プロパティ経由で設定

            // プロパティパネルの表示状態を設定
            PropertyPanel.Visibility = settings.VisiblePropertyPanel ? Visibility.Visible : Visibility.Collapsed;
            PropertySplitter.Visibility = settings.VisiblePropertyPanel ? Visibility.Visible : Visibility.Collapsed;

            // 保存されていた幅を読み込む
            _lastPropertyPanelWidth = settings.PropertyColumnWidth;

            // プロパティパネル列の幅を設定
            if (!settings.VisiblePropertyPanel)
            {
                MainGrid.ColumnDefinitions[1].Width = new GridLength(0);
                MainGrid.ColumnDefinitions[2].Width = new GridLength(0);
            }
            else if (settings.PropertyColumnWidth > 0)
            {
                MainGrid.ColumnDefinitions[1].Width = new GridLength(3);  // スプリッター
                MainGrid.ColumnDefinitions[2].Width = new GridLength(settings.PropertyColumnWidth);
            }

            // ウィンドウが表示された後に実行する処理
            Loaded += (s, e) => OnWindowLoaded();
        }

        private async void LoadImagePropertiesAsync(string filePath)
        {
            try
            {
                // ファイルからプロパティを読み込む
                var newProperties = await ImagePropertiesModel.LoadFromFileAsync(filePath);

                // データベースからレーティングを読み込んで設定
                var dbManager = new DatabaseManager();
                var fileNode = await dbManager.GetFileNodeAsync(filePath);
                if (fileNode != null)
                {
                    newProperties.Rating = fileNode.Rating;
                }

                Properties = newProperties;  // このセッターで PropertyChanged が発火する
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image properties: {ex.Message}");
                MessageBox.Show($"画像プロパティの読み込みに失敗しました：{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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

            // ボタンの表示状態を更新
            UpdateButtonVisibility();

            // フォーカスを確実に設定
            await Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                Activate(); // ウィンドウをアクティブにする
                Focus();    // ウィンドウにフォーカスを設定
                MainImage.Focus(); // 画像にフォーカス
            }));

            // 初期キャッシュの設定
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
            if (e.Key == Key.Tab)
            {
                e.Handled = true; // `Tab` キーでのフォーカス移動を抑止
            }
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
            // レーティング設定のショートカットキー
            else if (e.Key >= Key.D1 && e.Key <= Key.D5) // メインの数字キー
            {
                SetRating(e.Key - Key.D1 + 1);
                e.Handled = true;
            }
            else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad5) // テンキー
            {
                SetRating(e.Key - Key.NumPad1 + 1);
                e.Handled = true;
            }
            else if (e.Key == Key.D0 || e.Key == Key.NumPad0 || e.Key == Key.X) // レーティングをクリア
            {
                SetRating(0);
                e.Handled = true;
            }
            else if (e.Key == Key.Z)
            {
                SetRating(5);
                e.Handled = true;
            }
            else if (e.Key == Key.D)
            {
                DeleteCurrentImage();
                e.Handled = true;
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

                // データベースを更新
                var dbManager = new DatabaseManager();
                await dbManager.UpdateRatingAsync(_currentFilePath, rating);

                // イベントを発行して他の画面に通知
                var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                eventAggregator?.GetEvent<RatingChangedEvent>()?.Publish(
                    new RatingChangedEventArgs { FilePath = _currentFilePath, Rating = rating });
            }
        }

        private void TogglePropertyPanel()
        {
            if (PropertyPanel.Visibility == Visibility.Visible)
            {
                // プロパティパネル・スプリッターを非表示にする前に現在の幅を保存
                _lastPropertyPanelWidth = MainGrid.ColumnDefinitions[2].ActualWidth;

                // 幅が0以下の場合はデフォルト値を設定
                if (_lastPropertyPanelWidth <= 0)
                {
                    _lastPropertyPanelWidth = 250;
                }

                // プロパティパネル・スプリッターを非表示にする
                PropertyPanel.Visibility = Visibility.Collapsed;
                PropertySplitter.Visibility = Visibility.Collapsed;

                // カラムの幅を0に設定
                MainGrid.ColumnDefinitions[1].Width = new GridLength(0);
                MainGrid.ColumnDefinitions[2].Width = new GridLength(0);
            }
            else
            {
                // プロパティパネルを表示する
                PropertyPanel.Visibility = Visibility.Visible;
                PropertySplitter.Visibility = Visibility.Visible;

                // カラムの幅を復元
                MainGrid.ColumnDefinitions[1].Width = new GridLength(3);  // スプリッター

                // 保存していた幅または設定から幅を取得
                double panelWidth = _lastPropertyPanelWidth;

                // 幅が0以下の場合は、設定から取得または既定値を使用
                if (panelWidth <= 0)
                {
                    var settings = ViewerSettingsHelper.LoadSettings();
                    panelWidth = settings.PropertyColumnWidth > 0 ? settings.PropertyColumnWidth : 250;
                }

                MainGrid.ColumnDefinitions[2].Width = new GridLength(panelWidth);
            }

            // 設定を保存
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

        private void LoadImageFromCache(string filePath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"LoadImageFromCache: {filePath}");
                ImageSource = _imageCache.GetImage(filePath);
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
                System.Diagnostics.Debug.WriteLine($"LoadNewImage開始: {filePath}");

                // 現在のファイルパスを更新
                _currentFilePath = filePath;

                // まずキャッシュから読み込みを試みる
                LoadImageFromCache(filePath);

                // プロパティの読み込みと設定
                LoadImagePropertiesAsync(filePath);

                // キャッシュの更新
                var viewModel = Parent?.GetViewModel();
                if (viewModel != null)
                {
                    var files = viewModel.FilteredItems.Cast<FileNodeModel>().ToList();
                    var currentIndex = files.FindIndex(f => f.FullPath == filePath);
                    if (currentIndex >= 0)
                    {
                        _imageCache.UpdateCache(files, currentIndex);
                    }
                }

                // 親ウィンドウのサムネイル選択を更新
                Parent?.SyncThumbnailSelection(filePath);

                System.Diagnostics.Debug.WriteLine($"LoadNewImage完了: {filePath}");
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

        private void UpdateButtonVisibility()
        {
            // フルスクリーンモードに応じてボタンの表示/非表示を切り替え
            EnterFullScreenButton.Visibility = IsFullScreen ? Visibility.Collapsed : Visibility.Visible;
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
                IsFullScreen = true; // プロパティ経由で設定

                // 状態変更をすぐに保存
                SaveCurrentSettings();
            }
            else
            {
                // 保存していた状態に戻す
                WindowStyle = _previousWindowStyle;
                WindowState = _previousWindowState;
                IsFullScreen = false; // プロパティ経由で設定

                // タイトルバーを非表示
                TitleBar.Visibility = Visibility.Collapsed;
                _titleBarTimer.Stop();

                // マウスカーソルを表示状態に戻す
                Mouse.OverrideCursor = Cursors.Arrow;
                hideCursorTimer.Stop();

                // 状態変更をすぐに保存
                SaveCurrentSettings();
            }

            // ボタンの表示状態を更新
            UpdateButtonVisibility();
        }

        // 現在のウィンドウ設定を保存する共通メソッド
        private void SaveCurrentSettings()
        {
            // プロパティパネルの幅を取得（非表示の場合は前回保存した値を使用）
            double propertyWidth = PropertyPanel.Visibility == Visibility.Visible
                ? MainGrid.ColumnDefinitions[2].ActualWidth
                : _lastPropertyPanelWidth;

            var settings = new ViewerSettings
            {
                Left = _isFullScreen ? double.NaN : Left,
                Top = _isFullScreen ? double.NaN : Top,
                Width = _isFullScreen ? 800 : Width,
                Height = _isFullScreen ? 600 : Height,
                IsFullScreen = _isFullScreen,
                VisiblePropertyPanel = PropertyPanel.Visibility == Visibility.Visible,
                PropertyColumnWidth = propertyWidth > 0 ? propertyWidth : 250 // 値が0以下の場合はデフォルト値を使用
            };
            ViewerSettingsHelper.SaveSettings(settings);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
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

        private void MainGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isFullScreen) return;

            // マウスカーソルを表示
            Mouse.OverrideCursor = null; // nullに設定することでデフォルトのカーソルに戻す

            // カーソル非表示タイマーをリセット
            hideCursorTimer.Stop();
            hideCursorTimer.Start();
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
                    LoadNewImage(nextFilePath);
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

        private void OnRatingChanged(RatingChangedEventArgs args)
        {
            if (args.FilePath == _currentFilePath && Properties != null)
            {
                Properties.Rating = args.Rating;
            }
        }
    }
}
