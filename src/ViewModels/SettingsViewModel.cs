using System;
using System.Windows;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Illustra.Services;
using System.Diagnostics;

namespace Illustra.ViewModels
{
    public class SettingsViewModel : BindableBase
    {
        private readonly LanguageService _languageService;
        private readonly IEventAggregator _eventAggregator;

        private int _selectedLanguageIndex;
        private string _webUIUrl = "http://127.0.0.1:7860/sdapi/v1/txt2img"; // デフォルトURL

        public int SelectedLanguageIndex
        {
            get => _selectedLanguageIndex;
            set => SetProperty(ref _selectedLanguageIndex, value);
        }

        public string WebUIUrl
        {
            get => _webUIUrl;
            set => SetProperty(ref _webUIUrl, value);
        }

        public DelegateCommand SaveCommand { get; private set; }
        public DelegateCommand CancelCommand { get; private set; }

        public SettingsViewModel(LanguageService languageService, IEventAggregator eventAggregator)
        {
            _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));

            // コマンドの初期化
            SaveCommand = new DelegateCommand(ExecuteSave);
            CancelCommand = new DelegateCommand(ExecuteCancel);

            // 現在の言語設定に基づいてコンボボックスの選択インデックスを設定
            InitializeLanguageSelection();

            // デバッグ情報
            Debug.WriteLine($"SettingsViewModel initialized. Current language: {_languageService.GetCurrentLanguage()}, SelectedIndex: {SelectedLanguageIndex}");
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

                // ダイアログは表示しない
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ExecuteSave: {ex.Message}");
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteCancel()
        {
            // 設定画面を閉じる処理
            Debug.WriteLine("Cancel button clicked");
        }
    }
}
