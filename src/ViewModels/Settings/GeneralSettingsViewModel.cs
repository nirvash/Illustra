using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Illustra.Helpers;
using Illustra.Models;

namespace Illustra.ViewModels.Settings
{
    public class GeneralSettingsViewModel : SettingsViewModelBase
    {
        private readonly AppSettingsModel _settings;
        private readonly ViewerSettings _viewerSettings;
        private bool _deleteModePermanent;
        private bool _deleteModeRecycleBin;

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


        // 新規タブのフォルダ設定
        private bool _newTabFolderModeMyPictures;
        public bool NewTabFolderModeMyPictures
        {
            get => _newTabFolderModeMyPictures;
            set
            {
                if (_newTabFolderModeMyPictures != value)
                {
                    _newTabFolderModeMyPictures = value;
                    if (value) UpdateNewTabFolderMode(AppSettingsModel.NewTabFolderMode.MyPictures);
                    OnPropertyChanged(nameof(NewTabFolderModeMyPictures));
                }
            }
        }

        private bool _newTabFolderModeSpecified;
        public bool NewTabFolderModeSpecified
        {
            get => _newTabFolderModeSpecified;
            set
            {
                if (_newTabFolderModeSpecified != value)
                {
                    _newTabFolderModeSpecified = value;
                    if (value) UpdateNewTabFolderMode(AppSettingsModel.NewTabFolderMode.Specified);
                    OnPropertyChanged(nameof(NewTabFolderModeSpecified));
                }
            }
        }

        private string _newTabFolderPath = string.Empty;
        public string NewTabFolderPath
        {
            get => _newTabFolderPath;
            set
            {
                if (_newTabFolderPath != value)
                {
                    _newTabFolderPath = value;
                    OnPropertyChanged(nameof(NewTabFolderPath));
                }
            }
        }

        public ICommand BrowseNewTabFolderCommand { get; }

        public bool DeleteModePermanent
        {
            get => _deleteModePermanent;
            set
            {
                if (_deleteModePermanent != value)
                {
                    _deleteModePermanent = value;
                    OnPropertyChanged(nameof(DeleteModePermanent));
                    if (value)
                    {
                        _viewerSettings.DeleteMode = FileDeleteMode.Permanent;
                        DeleteModeRecycleBin = false;
                    }
                }
            }
        }

        public bool DeleteModeRecycleBin
        {
            get => _deleteModeRecycleBin;
            set
            {
                if (_deleteModeRecycleBin != value)
                {
                    _deleteModeRecycleBin = value;
                    OnPropertyChanged(nameof(DeleteModeRecycleBin));
                    if (value)
                    {
                        _viewerSettings.DeleteMode = FileDeleteMode.RecycleBin;
                        DeleteModePermanent = false;
                    }
                }
            }
        }

        public GeneralSettingsViewModel(AppSettingsModel settings, ViewerSettings viewerSettings)
        {
            _settings = settings;
            _viewerSettings = viewerSettings;
            BrowseStartupFolderCommand = new RelayCommand(BrowseStartupFolder);
            BrowseNewTabFolderCommand = new RelayCommand(BrowseNewTabFolder);
        }

        private void UpdateStartupMode(AppSettingsModel.StartupFolderMode mode)
        {
            StartupModeNone = mode == AppSettingsModel.StartupFolderMode.None;
            StartupModeLastOpened = mode == AppSettingsModel.StartupFolderMode.LastOpened;
            StartupModeSpecified = mode == AppSettingsModel.StartupFolderMode.Specified;
        }



        private void UpdateNewTabFolderMode(AppSettingsModel.NewTabFolderMode mode)
        {
            NewTabFolderModeMyPictures = mode == AppSettingsModel.NewTabFolderMode.MyPictures;
            NewTabFolderModeSpecified = mode == AppSettingsModel.NewTabFolderMode.Specified;
        }

        private void BrowseNewTabFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = Application.Current.FindResource("String_Dialog_SelectNewTabFolderTitle") as string ?? "Select Folder for New Tab"
            };

            if (dialog.ShowDialog() == true)
            {
                NewTabFolderPath = dialog.FolderName;
            }
        }
        private void BrowseStartupFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = Application.Current.FindResource("String_Dialog_SelectStartupFolderTitle") as string ?? "Select Startup Folder"
            };

            if (dialog.ShowDialog() == true)
            {
                StartupFolderPath = dialog.FolderName;
            }
        }

        public override void LoadSettings()
        {
            // AppSettingsの読み込み
            UpdateStartupMode(_settings.StartupMode);
            StartupFolderPath = _settings.StartupFolderPath;
            SelectLastFileOnStartup = _settings.SelectLastFileOnStartup;

            // ViewerSettingsの読み込み
            DeleteModePermanent = _viewerSettings.DeleteMode == FileDeleteMode.Permanent;
            DeleteModeRecycleBin = _viewerSettings.DeleteMode == FileDeleteMode.RecycleBin;

            // 新規タブフォルダ設定の読み込み
            UpdateNewTabFolderMode(_settings.NewTabFolderModeSetting);
            NewTabFolderPath = _settings.NewTabFolderPath;
        }

        public override void SaveSettings()
        {
            // AppSettingsの保存
            _settings.StartupMode = StartupModeNone ? AppSettingsModel.StartupFolderMode.None :
                                   StartupModeLastOpened ? AppSettingsModel.StartupFolderMode.LastOpened :
                                   AppSettingsModel.StartupFolderMode.Specified;
            _settings.StartupFolderPath = StartupFolderPath;
            _settings.SelectLastFileOnStartup = SelectLastFileOnStartup;

            // ViewerSettingsの保存
            _viewerSettings.DeleteMode = DeleteModeRecycleBin ? FileDeleteMode.RecycleBin : FileDeleteMode.Permanent;

            // 新規タブフォルダ設定の保存
            _settings.NewTabFolderModeSetting = NewTabFolderModeMyPictures ? AppSettingsModel.NewTabFolderMode.MyPictures :
                                           AppSettingsModel.NewTabFolderMode.Specified;
            _settings.NewTabFolderPath = NewTabFolderPath;
        }

        public override bool ValidateSettings()
        {
            if (StartupModeSpecified && string.IsNullOrWhiteSpace(StartupFolderPath))
            {
                return false;
            }
            if (NewTabFolderModeSpecified && string.IsNullOrWhiteSpace(NewTabFolderPath))
            {
                return false;
            }
            return true;
        }
    }
}
