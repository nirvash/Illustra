using Illustra.Helpers;

namespace Illustra.ViewModels.Settings
{
    public class PropertyPanelSettingsViewModel : SettingsViewModelBase
    {
        private readonly ViewerSettings _settings;

        private bool _showFileName;
        public bool ShowFileName
        {
            get => _showFileName;
            set
            {
                if (_showFileName != value)
                {
                    _showFileName = value;
                    OnPropertyChanged(nameof(ShowFileName));
                }
            }
        }

        private bool _showRating;
        public bool ShowRating
        {
            get => _showRating;
            set
            {
                if (_showRating != value)
                {
                    _showRating = value;
                    OnPropertyChanged(nameof(ShowRating));
                }
            }
        }

        private bool _showDetails;
        public bool ShowDetails
        {
            get => _showDetails;
            set
            {
                if (_showDetails != value)
                {
                    _showDetails = value;
                    OnPropertyChanged(nameof(ShowDetails));
                }
            }
        }

        private bool _showStableDiffusion;
        public bool ShowStableDiffusion
        {
            get => _showStableDiffusion;
            set
            {
                if (_showStableDiffusion != value)
                {
                    _showStableDiffusion = value;
                    OnPropertyChanged(nameof(ShowStableDiffusion));
                }
            }
        }

        private bool _showComment;
        public bool ShowComment
        {
            get => _showComment;
            set
            {
                if (_showComment != value)
                {
                    _showComment = value;
                    OnPropertyChanged(nameof(ShowComment));
                }
            }
        }

        private double _fontSize = 12.0;
        public double FontSize
        {
            get => _fontSize;
            set
            {
                if (_fontSize != value && value >= 8.0 && value <= 24.0)
                {
                    _fontSize = value;
                    OnPropertyChanged(nameof(FontSize));
                }
            }
        }

        public PropertyPanelSettingsViewModel(ViewerSettings settings)
        {
            _settings = settings;
        }

        public override void LoadSettings()
        {
            ShowFileName = _settings.ShowFileName;
            ShowRating = _settings.ShowRating;
            ShowDetails = _settings.ShowDetails;
            ShowStableDiffusion = _settings.ShowStableDiffusion;
            ShowComment = _settings.ShowComment;
            FontSize = _settings.PropertyPanelFontSize;
        }

        public override void SaveSettings()
        {
            _settings.ShowFileName = ShowFileName;
            _settings.ShowRating = ShowRating;
            _settings.ShowDetails = ShowDetails;
            _settings.ShowStableDiffusion = ShowStableDiffusion;
            _settings.ShowComment = ShowComment;
            _settings.PropertyPanelFontSize = FontSize;
        }

        public override bool ValidateSettings()
        {
            return FontSize >= 8.0 && FontSize <= 24.0;
        }
    }
}
