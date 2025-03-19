using System;
using System.Windows;
using System.ComponentModel;
using System.Threading;
using Illustra.Helpers;
using System.Windows.Controls;
using Illustra.Models;

namespace Illustra.Views
{
    public partial class AdvancedSettingsWindow : Window, INotifyPropertyChanged
    {
        private double _mouseWheelMultiplier;
        public double MouseWheelMultiplier
        {
            get => _mouseWheelMultiplier;
            set
            {
                if (_mouseWheelMultiplier != value)
                {
                    _mouseWheelMultiplier = value;
                    OnPropertyChanged(nameof(MouseWheelMultiplier));
                }
            }
        }

        private double _slideshowInterval;
        public double SlideshowInterval
        {
            get => _slideshowInterval;
            set
            {
                if (_slideshowInterval != value)
                {
                    _slideshowInterval = value;
                    OnPropertyChanged(nameof(SlideshowInterval));
                }
            }
        }

        private bool _developerMode;
        public bool DeveloperMode
        {
            get => _developerMode;
            set
            {
                if (_developerMode != value)
                {
                    _developerMode = value;
                    OnPropertyChanged(nameof(DeveloperMode));
                }
            }
        }

        private bool _startupModeNone;
        public bool StartupModeNone
        {
            get => _startupModeNone;
            set
            {
                if (_startupModeNone != value)
                {
                    _startupModeNone = value;
                    if (value) UpdateStartupMode(AppSettingsModel.StartupFolderMode.None);
                    OnPropertyChanged(nameof(StartupModeNone));
                }
            }
        }

        private bool _startupModeLastOpened;
        public bool StartupModeLastOpened
        {
            get => _startupModeLastOpened;
            set
            {
                if (_startupModeLastOpened != value)
                {
                    _startupModeLastOpened = value;
                    if (value) UpdateStartupMode(AppSettingsModel.StartupFolderMode.LastOpened);
                    OnPropertyChanged(nameof(StartupModeLastOpened));
                }
            }
        }

        private bool _startupModeSpecified;
        public bool StartupModeSpecified
        {
            get => _startupModeSpecified;
            set
            {
                if (_startupModeSpecified != value)
                {
                    _startupModeSpecified = value;
                    if (value) UpdateStartupMode(AppSettingsModel.StartupFolderMode.Specified);
                    OnPropertyChanged(nameof(StartupModeSpecified));
                }
            }
        }

        private string _startupFolderPath = string.Empty;
        public string StartupFolderPath
        {
            get => _startupFolderPath;
            set
            {
                if (_startupFolderPath != value)
                {
                    _startupFolderPath = value;
                    OnPropertyChanged(nameof(StartupFolderPath));
                }
            }
        }

        private AppSettingsModel _settings;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateStartupMode(AppSettingsModel.StartupFolderMode mode)
        {
            _startupModeNone = mode == AppSettingsModel.StartupFolderMode.None;
            _startupModeLastOpened = mode == AppSettingsModel.StartupFolderMode.LastOpened;
            _startupModeSpecified = mode == AppSettingsModel.StartupFolderMode.Specified;
        }

        public AdvancedSettingsWindow()
        {
            InitializeComponent();
            _settings = SettingsHelper.GetSettings();
            DataContext = this;

            // 設定から値を読み込む
            DeveloperMode = _settings.DeveloperMode;
            SlideshowInterval = 5.0; // ViewerSettingsから取得する必要がある
            MouseWheelMultiplier = _settings.MouseWheelMultiplier;

            // 起動モードの設定
            UpdateStartupMode(_settings.StartupMode);
            StartupFolderPath = _settings.StartupFolderPath;

            // 開発者モードの状態に応じてログカテゴリ設定の表示/非表示を切り替え
            UpdateLogCategoriesVisibility();

            // 開発者モードが有効な場合はログカテゴリ設定を初期化
            if (_settings.DeveloperMode)
            {
                InitializeLogCategorySettings();
            }
        }

        private void DeveloperModeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateLogCategoriesVisibility();

            // 開発者モードが有効になった場合、ログカテゴリ設定を初期化
            if (DeveloperModeCheckBox.IsChecked == true && LogCategoriesPanel.Children.Count == 0)
            {
                InitializeLogCategorySettings();
            }
        }

        private void UpdateLogCategoriesVisibility()
        {
            // 開発者モードが有効な場合のみログカテゴリ設定を表示
            LogCategoriesExpander.Visibility =
                DeveloperModeCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void InitializeLogCategorySettings()
        {
            // 既存のチェックボックスをクリア
            LogCategoriesPanel.Children.Clear();

            // チェックボックスを動的に生成
            foreach (var categoryField in typeof(LogHelper.Categories).GetFields())
            {
                if (categoryField.IsLiteral && !categoryField.IsInitOnly)
                {
                    string categoryName = (string)categoryField.GetValue(null);
                    bool isEnabled = LogHelper.IsCategoryEnabled(categoryName);

                    var checkBox = new CheckBox
                    {
                        Content = categoryName,
                        IsChecked = isEnabled,
                        Margin = new Thickness(5),
                        Tag = categoryName
                    };

                    checkBox.Checked += LogCategory_CheckedChanged;
                    checkBox.Unchecked += LogCategory_CheckedChanged;

                    LogCategoriesPanel.Children.Add(checkBox);
                }
            }
        }

        private void LogCategory_CheckedChanged(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox != null && checkBox.Tag is string categoryName)
            {
                LogHelper.SetCategoryEnabled(categoryName, checkBox.IsChecked ?? false);
            }
        }

        private async void CleanupDatabase_Click(object sender, RoutedEventArgs e)
        {
            var progressDialog = new ProgressDialog()
            {
                Owner = this,
                WindowTitle = (string)FindResource("String_Settings_Developer_CleanupDatabase"),
                Message = (string)FindResource("String_Settings_Developer_PreparingCleanup")
            };

            var cancellationTokenSource = new CancellationTokenSource();
            progressDialog.CancelRequested += (s, e) => cancellationTokenSource.Cancel();
            progressDialog.StartRequested += async (s, e) =>
            {
                try
                {
                    var dbManager = new DatabaseManager();
                    progressDialog.IsIndeterminate = false;
                    var (deletedZeroRating, deletedMissing) = await Task.Run(() =>
                        dbManager.CleanupDatabaseAsync(progressDialog.UpdateProgress,
                                                       cancellationTokenSource.Token
                    ));

                    await progressDialog.Dispatcher.InvokeAsync(() => progressDialog.Close());

                    MessageBox.Show(
                        this,
                        string.Format(
                            (string)FindResource("String_Settings_Developer_CleanupComplete"),
                            deletedZeroRating,
                            deletedMissing
                        ),
                        (string)FindResource("String_Common_Information"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
                catch (OperationCanceledException)
                {
                    progressDialog.Close();
                    MessageBox.Show(
                        this,
                        (string)FindResource("String_Common_Cancelled"),
                        (string)FindResource("String_Common_Information"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
                catch (Exception ex)
                {
                    progressDialog.Close();
                    MessageBox.Show(
                        this,
                        $"{FindResource("String_Settings_Developer_CleanupError")}\n{ex.Message}",
                        (string)FindResource("String_Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            };

            progressDialog.Show();
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 入力値の検証
                if (SlideshowInterval <= 0)
                {
                    MessageBox.Show(
                        (string)FindResource("String_Settings_Slideshow_InvalidInterval"),
                        (string)FindResource("String_Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // 入力値の検証（マウスホイール倍率）
                if (MouseWheelMultiplier <= 0)
                {
                    MessageBox.Show(
                        "マウスホイール倍率は0より大きい値を入力してください。",
                        (string)FindResource("String_Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // 設定を保存
                _settings.DeveloperMode = DeveloperMode;
                _settings.StartupMode = _startupModeNone ? AppSettingsModel.StartupFolderMode.None :
                                      _startupModeLastOpened ? AppSettingsModel.StartupFolderMode.LastOpened :
                                      AppSettingsModel.StartupFolderMode.Specified;
                _settings.StartupFolderPath = StartupFolderPath;
                _settings.MouseWheelMultiplier = MouseWheelMultiplier;

                SettingsHelper.SaveSettings(_settings);

                // ログカテゴリ設定も保存
                if (_settings.DeveloperMode)
                {
                    LogHelper.SaveCategorySettings();
                }

                var viewerSettings = ViewerSettingsHelper.LoadSettings();
                viewerSettings.SlideshowIntervalSeconds = SlideshowInterval;
                ViewerSettingsHelper.SaveSettings(viewerSettings);

                // メインウィンドウのメニュー表示を更新
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.UpdateToolsMenuVisibility();

                    // ThumbnailListControlの設定を更新
                    var thumbnailList = UIHelper.FindVisualChild<ThumbnailListControl>(mainWindow);
                    if (thumbnailList != null)
                    {
                        thumbnailList.ApplySettings();
                        LogHelper.LogWithTimestamp($"マウスホイール倍率を更新: {MouseWheelMultiplier:F1}", LogHelper.Categories.UI);
                    }
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{FindResource("String_Settings_SaveError")}\n{ex.Message}",
                    (string)FindResource("String_Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BrowseStartupFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = (string)FindResource("String_Settings_Startup_Section")
            };

            if (dialog.ShowDialog() == true)
            {
                StartupFolderPath = dialog.FolderName;
            }
        }
    }
}
