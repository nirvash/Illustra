using System.Windows.Input;
using Microsoft.Win32;
using Illustra.Helpers;
using Illustra.Models;

namespace Illustra.ViewModels.Settings
{
    public class GeneralSettingsViewModel : SettingsViewModelBase
    {
        private readonly AppSettingsModel _settings;

        private bool _selectLastFileOnStartup;
        public bool SelectLastFileOnStartup
        {
            get => _selectLastFileOnStartup;
            set
            {
                if (_selectLastFileOnStartup != value)
                {
                    _selectLastFileOnStartup = value;
                    OnPropertyChanged(nameof(SelectLastFileOnStartup));
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

        public ICommand BrowseStartupFolderCommand { get; }

        public GeneralSettingsViewModel(AppSettingsModel settings)
        {
            _settings = settings;
            BrowseStartupFolderCommand = new RelayCommand(BrowseStartupFolder);
        }

        private void UpdateStartupMode(AppSettingsModel.StartupFolderMode mode)
        {
            StartupModeNone = mode == AppSettingsModel.StartupFolderMode.None;
            StartupModeLastOpened = mode == AppSettingsModel.StartupFolderMode.LastOpened;
            StartupModeSpecified = mode == AppSettingsModel.StartupFolderMode.Specified;
        }

        private void BrowseStartupFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "起動時のフォルダを選択"
            };

            if (dialog.ShowDialog() == true)
            {
                StartupFolderPath = dialog.FolderName;
            }
        }

        public override void LoadSettings()
        {
            UpdateStartupMode(_settings.StartupMode);
            StartupFolderPath = _settings.StartupFolderPath;
            SelectLastFileOnStartup = _settings.SelectLastFileOnStartup;
        }

        public override void SaveSettings()
        {
            _settings.StartupMode = StartupModeNone ? AppSettingsModel.StartupFolderMode.None :
                                   StartupModeLastOpened ? AppSettingsModel.StartupFolderMode.LastOpened :
                                   AppSettingsModel.StartupFolderMode.Specified;
            _settings.StartupFolderPath = StartupFolderPath;
            _settings.SelectLastFileOnStartup = SelectLastFileOnStartup;
        }

        public override bool ValidateSettings()
        {
            if (StartupModeSpecified && string.IsNullOrWhiteSpace(StartupFolderPath))
            {
                return false;
            }
            return true;
        }
    }
}
