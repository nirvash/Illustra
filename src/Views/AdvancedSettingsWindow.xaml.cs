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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public AdvancedSettingsWindow()
        {
            InitializeComponent();
            DataContext = this;

            // 現在の設定を読み込む
            var settings = SettingsHelper.GetSettings();
            DeveloperMode = settings.DeveloperMode;
            SlideshowInterval = ViewerSettingsHelper.LoadSettings().SlideshowIntervalSeconds;
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
                var viewerSettings = ViewerSettingsHelper.LoadSettings();
                viewerSettings.SlideshowIntervalSeconds = SlideshowInterval;
                ViewerSettingsHelper.SaveSettings(viewerSettings);

                var settings = SettingsHelper.GetSettings();
                settings.DeveloperMode = DeveloperMode;
                SettingsHelper.SaveSettings(settings);

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
