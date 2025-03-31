using System;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.Views;
using Illustra.ViewModels;
using Prism.Ioc;
using System.Threading;

namespace Illustra.ViewModels.Settings
{
    public class DeveloperSettingsViewModel : SettingsViewModelBase
    {
        private readonly AppSettingsModel _settings;
        private readonly DatabaseManager _databaseManager;

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
                    UpdateLogCategoriesVisibility();
                }
            }
        }

        private bool _enableMcpHost;
        public bool EnableMcpHost
        {
            get => _enableMcpHost;
            set
            {
                if (_enableMcpHost != value)
                {
                    _enableMcpHost = value;
                    OnPropertyChanged(nameof(EnableMcpHost));
                }
            }
        }


        public ObservableCollection<LogCategoryItem> LogCategories { get; } = new();

        public ICommand CleanupDatabaseCommand { get; }

        public DeveloperSettingsViewModel(AppSettingsModel settings)
        {
            _settings = settings;
            _databaseManager = ContainerLocator.Container.Resolve<DatabaseManager>();
            CleanupDatabaseCommand = new RelayCommand(CleanupDatabase); // Rename method call
        }

        private void UpdateLogCategoriesVisibility()
        {
            if (DeveloperMode && LogCategories.Count == 0)
            {
                InitializeLogCategories();
            }
        }

        private void InitializeLogCategories()
        {
            LogCategories.Clear();
            foreach (var categoryField in typeof(LogHelper.Categories).GetFields())
            {
                if (categoryField.IsLiteral && !categoryField.IsInitOnly)
                {
                    string categoryName = (string)categoryField.GetValue(null);
                    LogCategories.Add(new LogCategoryItem
                    {
                        Name = categoryName,
                        IsEnabled = LogHelper.IsCategoryEnabled(categoryName)
                    });
                }
            }
        }

        private void CleanupDatabase() // Rename method definition
        {
            try
            {
                var progressDialog = new ProgressDialog()
                {
                    Owner = Application.Current.MainWindow,
                    WindowTitle = (string)Application.Current.FindResource("String_Settings_Developer_CleanupDatabase"),
                    Message = (string)Application.Current.FindResource("String_Settings_Developer_PreparingCleanup")
                };

                var cancellationTokenSource = new CancellationTokenSource();
                progressDialog.CancelRequested += (s, e) => cancellationTokenSource.Cancel();
                progressDialog.StartRequested += async (s, e) =>
                {
                    try
                    {
                        progressDialog.IsIndeterminate = false;
                        var (deletedZeroRating, deletedMissing) = await Task.Run(() =>
                            _databaseManager.CleanupDatabaseAsync(progressDialog.UpdateProgress,
                                                       cancellationTokenSource.Token
                        ));

                        await progressDialog.Dispatcher.InvokeAsync(() => progressDialog.Close());

                        MessageBox.Show(
                            string.Format(
                                (string)Application.Current.FindResource("String_Settings_Developer_CleanupComplete"),
                                deletedZeroRating,
                                deletedMissing
                            ),
                            (string)Application.Current.FindResource("String_Common_Information"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                    catch (OperationCanceledException)
                    {
                        progressDialog.Close();
                        MessageBox.Show(
                            (string)Application.Current.FindResource("String_Common_Cancelled"),
                            (string)Application.Current.FindResource("String_Common_Information"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                    catch (Exception ex)
                    {
                        progressDialog.Close();
                        MessageBox.Show(
                            $"{Application.Current.FindResource("String_Settings_Developer_CleanupError")}\n{ex.Message}",
                            (string)Application.Current.FindResource("String_Error"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                };

                progressDialog.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{Application.Current.FindResource("String_Settings_Developer_CleanupError")}\n{ex.Message}",
                    (string)Application.Current.FindResource("String_Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        public override void LoadSettings()
        {
            DeveloperMode = _settings.DeveloperMode;
            EnableMcpHost = _settings.EnableMcpHost;
            if (DeveloperMode)
            {
                InitializeLogCategories();
            }
        }

        public override void SaveSettings()
        {
            _settings.DeveloperMode = DeveloperMode;
            _settings.EnableMcpHost = EnableMcpHost;
            if (DeveloperMode)
            {
                foreach (var category in LogCategories)
                {
                    LogHelper.SetCategoryEnabled(category.Name, category.IsEnabled);
                }
                LogHelper.SaveCategorySettings();
            }
        }

        public override bool ValidateSettings()
        {
            return true; // 開発者設定には特別な検証は不要
        }
    }
}
