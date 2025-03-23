using System.ComponentModel;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.Views;
using Illustra.Controls;
using Illustra.Events;

namespace Illustra.ViewModels.Settings
{
    public class AdvancedSettingsViewModel : INotifyPropertyChanged
    {
        private readonly AppSettingsModel _settings;
        private readonly ViewerSettings _viewerSettings;

        public GeneralSettingsViewModel GeneralSettings { get; }
        public ThumbnailSettingsViewModel ThumbnailSettings { get; }
        public ViewerSettingsViewModel ViewerSettings { get; }
        public PropertyPanelSettingsViewModel PropertyPanelSettings { get; }
        public DeveloperSettingsViewModel DeveloperSettings { get; }

        public AdvancedSettingsViewModel()
        {
            _settings = SettingsHelper.GetSettings();
            _viewerSettings = ViewerSettingsHelper.LoadSettings();

            // 各カテゴリのViewModelを初期化
            GeneralSettings = new GeneralSettingsViewModel(_settings, _viewerSettings);
            ThumbnailSettings = new ThumbnailSettingsViewModel(_settings);
            ViewerSettings = new ViewerSettingsViewModel(_viewerSettings);
            PropertyPanelSettings = new PropertyPanelSettingsViewModel(_viewerSettings);
            DeveloperSettings = new DeveloperSettingsViewModel(_settings);

            // 設定を読み込む
            LoadSettings();
        }

        public void LoadSettings()
        {
            GeneralSettings.LoadSettings();
            ThumbnailSettings.LoadSettings();
            ViewerSettings.LoadSettings();
            PropertyPanelSettings.LoadSettings();
            DeveloperSettings.LoadSettings();
        }

        public bool ValidateSettings()
        {
            return GeneralSettings.ValidateSettings() &&
                   ThumbnailSettings.ValidateSettings() &&
                   ViewerSettings.ValidateSettings() &&
                   PropertyPanelSettings.ValidateSettings() &&
                   DeveloperSettings.ValidateSettings();
        }

        public void SaveSettings()
        {
            GeneralSettings.SaveSettings();
            ThumbnailSettings.SaveSettings();
            ViewerSettings.SaveSettings();
            PropertyPanelSettings.SaveSettings();
            DeveloperSettings.SaveSettings();

            // 基本設定の保存
            SettingsHelper.SaveSettings(_settings);

            // ビューワ設定の保存
            ViewerSettingsHelper.SaveSettings(_viewerSettings);

            // UI更新
            UpdateUI();
        }

        private void UpdateUI()
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                // メインウィンドウのメニュー表示を更新
                mainWindow.UpdateToolsMenuVisibility();

                // ThumbnailListControlの設定を更新
                var thumbnailList = UIHelper.FindVisualChild<ThumbnailListControl>(mainWindow);
                if (thumbnailList != null)
                {
                    thumbnailList.ApplySettings();
                    LogHelper.LogWithTimestamp($"マウスホイール倍率を更新: {ThumbnailSettings.MouseWheelMultiplier:F1}", LogHelper.Categories.UI);
                }

                var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                eventAggregator.GetEvent<ViewerSettingsChangedEvent>().Publish();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
