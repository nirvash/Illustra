using Illustra.Helpers;

namespace Illustra.ViewModels.Settings
{
    public class ViewerSettingsViewModel : SettingsViewModelBase
    {
        private readonly ViewerSettings _settings;

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

        public ViewerSettingsViewModel(ViewerSettings settings)
        {
            _settings = settings;
        }

        public override void LoadSettings()
        {
            SlideshowInterval = _settings.SlideshowIntervalSeconds;
        }

        public override void SaveSettings()
        {
            _settings.SlideshowIntervalSeconds = SlideshowInterval;
        }

        public override bool ValidateSettings()
        {
            return SlideshowInterval > 0;
        }
    }
}
