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
        }

        public override void SaveSettings()
        {
            _settings.ShowFileName = ShowFileName;
            _settings.ShowRating = ShowRating;
            _settings.ShowDetails = ShowDetails;
            _settings.ShowStableDiffusion = ShowStableDiffusion;
            _settings.ShowComment = ShowComment;
        }

        public override bool ValidateSettings()
        {
            return true;
        }
    }
}
