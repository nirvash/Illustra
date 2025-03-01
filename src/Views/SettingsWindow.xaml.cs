using System.Windows;
using Illustra.Helpers;

namespace Illustra.Views
{
    public partial class SettingsWindow : Window
    {
        private AppSettings _settings;

        public SettingsWindow()
        {
            InitializeComponent();

            // 現在の設定を読み込む
            _settings = SettingsHelper.GetSettings();

            // UIに設定値を反映
            LoadSettingsToUI();
        }

        /// <summary>
        /// 設定値をUIに反映する
        /// </summary>
        private void LoadSettingsToUI()
        {
            // サムネイル設定
            DefaultThumbnailSizeSlider.Value = _settings.ThumbnailSize;

            // スクロール設定
            MouseWheelMultiplierSlider.Value = _settings.MouseWheelMultiplier;

            // ビューア設定
            SaveViewerStateCheckBox.IsChecked = _settings.SaveViewerState;
        }

        /// <summary>
        /// UIから設定値を取得して保存する
        /// </summary>
        private void SaveSettingsFromUI()
        {
            // サムネイル設定
            _settings.ThumbnailSize = (int)DefaultThumbnailSizeSlider.Value;

            // スクロール設定
            _settings.MouseWheelMultiplier = MouseWheelMultiplierSlider.Value;

            // ビューア設定
            _settings.SaveViewerState = SaveViewerStateCheckBox.IsChecked ?? true;

            // 設定を保存
            SettingsHelper.SaveSettings(_settings);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettingsFromUI();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
