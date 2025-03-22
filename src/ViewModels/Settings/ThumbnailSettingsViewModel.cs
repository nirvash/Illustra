using Illustra.Helpers;
using Illustra.Models;

namespace Illustra.ViewModels.Settings
{
    public class ThumbnailSettingsViewModel : SettingsViewModelBase
    {
        private readonly AppSettingsModel _settings;

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

        public ThumbnailSettingsViewModel(AppSettingsModel settings)
        {
            _settings = settings;
        }

        public override void LoadSettings()
        {
            MouseWheelMultiplier = _settings.MouseWheelMultiplier;
        }

        public override void SaveSettings()
        {
            _settings.MouseWheelMultiplier = MouseWheelMultiplier;
        }

        public override bool ValidateSettings()
        {
            return MouseWheelMultiplier > 0;
        }
    }
}
