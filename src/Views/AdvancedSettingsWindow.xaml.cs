using System;
using System.Windows;
using System.ComponentModel;
using Illustra.Helpers;

namespace Illustra.Views
{
    public partial class AdvancedSettingsWindow : Window, INotifyPropertyChanged
    {
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
                    if (value) UpdateStartupMode(AppSettings.StartupFolderMode.None);
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
                    if (value) UpdateStartupMode(AppSettings.StartupFolderMode.LastOpened);
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
                    if (value) UpdateStartupMode(AppSettings.StartupFolderMode.Specified);
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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateStartupMode(AppSettings.StartupFolderMode mode)
        {
            _startupModeNone = mode == AppSettings.StartupFolderMode.None;
            _startupModeLastOpened = mode == AppSettings.StartupFolderMode.LastOpened;
            _startupModeSpecified = mode == AppSettings.StartupFolderMode.Specified;
        }

        public AdvancedSettingsWindow()
        {
            InitializeComponent();
            DataContext = this;

            // 現在の設定を読み込む
            var settings = SettingsHelper.GetSettings();
            DeveloperMode = settings.DeveloperMode;
            SlideshowInterval = ViewerSettingsHelper.LoadSettings().SlideshowIntervalSeconds;

            // スタートアップ設定を読み込む
            UpdateStartupMode(settings.StartupMode);
            StartupFolderPath = settings.StartupFolderPath;
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

                // 設定を保存
                var settings = SettingsHelper.GetSettings();
                settings.DeveloperMode = DeveloperMode;

                // スタートアップモードを判定して保存
                if (StartupModeNone)
                    settings.StartupMode = AppSettings.StartupFolderMode.None;
                else if (StartupModeLastOpened)
                    settings.StartupMode = AppSettings.StartupFolderMode.LastOpened;
                else
                    settings.StartupMode = AppSettings.StartupFolderMode.Specified;

                settings.StartupFolderPath = StartupFolderPath;
                SettingsHelper.SaveSettings(settings);

                var viewerSettings = ViewerSettingsHelper.LoadSettings();
                viewerSettings.SlideshowIntervalSeconds = SlideshowInterval;
                ViewerSettingsHelper.SaveSettings(viewerSettings);

                // メインウィンドウのメニュー表示を更新
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.UpdateToolsMenuVisibility();
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
    }
}
