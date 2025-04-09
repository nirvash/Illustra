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

        private bool _fitSmallAnimationToScreen;
        public bool FitSmallAnimationToScreen
        {
            get => _fitSmallAnimationToScreen;
            set
            {
                if (_fitSmallAnimationToScreen != value)
                {
                    _fitSmallAnimationToScreen = value;
                    OnPropertyChanged(nameof(FitSmallAnimationToScreen));
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
            ShowFileName = _settings.ShowFileName;
            ShowRating = _settings.ShowRating;
            ShowDetails = _settings.ShowDetails;
            ShowStableDiffusion = _settings.ShowStableDiffusion;
            ShowComment = _settings.ShowComment;
            FitSmallAnimationToScreen = _settings.FitSmallAnimationToScreen;
        }

        public override void SaveSettings()
        {
            _settings.SlideshowIntervalSeconds = SlideshowInterval;
            _settings.ShowFileName = ShowFileName;
            _settings.ShowRating = ShowRating;
            _settings.ShowDetails = ShowDetails;
            _settings.ShowStableDiffusion = ShowStableDiffusion;
            _settings.ShowComment = ShowComment;
            _settings.FitSmallAnimationToScreen = FitSmallAnimationToScreen;
        }

        public override bool ValidateSettings()
        {
            return SlideshowInterval > 0;
        }
    }
}
