using System;
using System.Windows;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Illustra.Services;
using System.Diagnostics;

namespace Illustra.ViewModels
{
    public class LanguageSettingsViewModel : BindableBase
    {
        private readonly LanguageService _languageService;
        private readonly IEventAggregator _eventAggregator;
        private int _selectedLanguageIndex;

        public int SelectedLanguageIndex
        {
            get => _selectedLanguageIndex;
            set => SetProperty(ref _selectedLanguageIndex, value);
        }

        public DelegateCommand SaveCommand { get; private set; }
        public DelegateCommand CancelCommand { get; private set; }

        public LanguageSettingsViewModel(LanguageService languageService, IEventAggregator eventAggregator)
        {
            _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));

            SaveCommand = new DelegateCommand(ExecuteSave);
            CancelCommand = new DelegateCommand(ExecuteCancel);

            InitializeLanguageSelection();
        }

        private void InitializeLanguageSelection()
        {
            try
            {
                var currentLanguage = _languageService.GetCurrentLanguage();
                Debug.WriteLine($"Current language from service: {currentLanguage}");

                // 明示的に言語コードをチェックして選択インデックスを設定
                if (currentLanguage == "ja")
                {
                    SelectedLanguageIndex = 1; // 日本語
                    Debug.WriteLine("Setting language index to 1 (Japanese)");
                }
                else
                {
                    SelectedLanguageIndex = 0; // 英語
                    Debug.WriteLine("Setting language index to 0 (English)");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in InitializeLanguageSelection: {ex.Message}");
                // エラーが発生した場合はデフォルト値を設定
                SelectedLanguageIndex = 0;
            }
        }

        private void ExecuteSave()
        {
            try
            {
                // 選択された言語コードを取得
                string languageCode = SelectedLanguageIndex == 1 ? "ja" : "en";
                Debug.WriteLine($"Saving language setting: {languageCode}, SelectedIndex: {SelectedLanguageIndex}");

                // 言語を設定
                _languageService.SetLanguage(languageCode);

                MessageBox.Show(
                    (string)Application.Current.FindResource("String_Settings_Language_SavedMessage"),
                    (string)Application.Current.FindResource("String_Settings_Language_SavedTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ExecuteSave: {ex.Message}");
                MessageBox.Show(
                    string.Format((string)Application.Current.FindResource("String_Settings_Error_SaveFailed"), ex.Message),
                    (string)Application.Current.FindResource("String_Settings_Error_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ExecuteCancel()
        {
            // 設定をキャンセル
            Debug.WriteLine("Cancel button clicked");
        }
    }
}
