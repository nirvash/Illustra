using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Illustra.Helpers;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Concurrent;

namespace Illustra.Views
{
    public partial class MainWindow : Window
    {
        private ThumbnailLoaderHelper _thumbnailLoader;
        private bool _isInitialized = false;
        private AppSettings _appSettings;
        private bool _isLastFolderLoaded = false;

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

            // サムネイルローダーの初期化
            _thumbnailLoader = new ThumbnailLoaderHelper(ThumbnailListBox);

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

                        // 選択に成功しなかった場合は、直接サムネイルを読み込む
                        if (!selected && !_isLastFolderLoaded)
                        {
                            _isLastFolderLoaded = true;
                            _thumbnailLoader.LoadThumbnails(_appSettings.LastFolderPath);
                        }

                        // 選択状態を明示的に再設定
                        await Task.Delay(100);
                        if (FolderTreeView.SelectedItem is TreeViewItem selectedItem &&
                            selectedItem.Tag is string path &&
                            path == _appSettings.LastFolderPath)
                        {
                            selectedItem.IsSelected = true;
                            selectedItem.Focus();
                            selectedItem.BringIntoView();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"前回のフォルダを開く際にエラーが発生: {ex.Message}");
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

            // 設定を保存
            SettingsHelper.SaveSettings(_appSettings);
        }

        private async void LoadDrivesAsync()
        {
            var drives = await FileSystemHelper.LoadDrivesAsync();
            foreach (var driveNode in drives)
            {
                Dispatcher.Invoke(() => FolderTreeView.Items.Add(FileSystemHelper.CreateDriveNode(driveNode)));
            }
        }

        private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem selectedItem && selectedItem.Tag is string folderPath)
            {
                if (folderPath != null)
                {
                    _isLastFolderLoaded = true;
                    _thumbnailLoader.LoadThumbnails(folderPath);
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
    }
}
