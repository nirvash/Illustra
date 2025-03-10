using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using Prism.Events;
using System.IO;
using Newtonsoft.Json;
using System.Diagnostics;

namespace Illustra.Services
{
    public class LanguageChangedEvent : PubSubEvent { }

    public class LanguageService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly string _configPath;
        private string _currentLanguage;

        public LanguageService(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Illustra",
                "config.json");

            // 設定ディレクトリが存在しない場合は作成
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath));

            // 初期化時に保存された言語設定を読み込む
            LoadLanguageSetting();

            Debug.WriteLine($"LanguageService initialized. Current language: {GetCurrentLanguage()}");
        }

        public void SetLanguage(string languageCode)
        {
            Debug.WriteLine($"SetLanguage called with: {languageCode}");

            // 言語コードを検証
            if (languageCode != "en" && languageCode != "ja")
            {
                Debug.WriteLine($"Invalid language code: {languageCode}, defaulting to 'en'");
                languageCode = "en"; // デフォルトは英語
            }

            try
            {
                // 現在のスレッドのカルチャを設定
                var culture = new CultureInfo(languageCode);
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;

                // 現在の言語を保存
                _currentLanguage = languageCode;

                Debug.WriteLine($"Culture set to: {Thread.CurrentThread.CurrentUICulture.Name}");

                // 設定を保存
                SaveLanguageSetting(languageCode);

                // 言語変更イベントを発行
                _eventAggregator.GetEvent<LanguageChangedEvent>().Publish();

                Debug.WriteLine("Language change event published");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SetLanguage: {ex.Message}");
                // エラーが発生した場合はログに記録
            }
        }

        public string GetCurrentLanguage()
        {
            // 明示的に保存された言語設定を返す
            if (!string.IsNullOrEmpty(_currentLanguage))
            {
                return _currentLanguage;
            }

            // 保存された設定がない場合はスレッドのカルチャから取得
            var language = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;
            Debug.WriteLine($"GetCurrentLanguage returning: {language}");
            return language;
        }

        private void LoadLanguageSetting()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    Debug.WriteLine($"Loading language settings from: {_configPath}");
                    var json = File.ReadAllText(_configPath);
                    var config = JsonConvert.DeserializeObject<AppConfig>(json);

                    string languageCode = config?.Language ?? "en";
                    Debug.WriteLine($"Loaded language code: {languageCode}");

                    // 現在の言語を保存
                    _currentLanguage = languageCode;

                    SetLanguage(languageCode);
                }
                else
                {
                    Debug.WriteLine("Config file not found, using system language");
                    // 設定ファイルがない場合はシステム言語を取得
                    var systemLanguage = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
                    Debug.WriteLine($"System language: {systemLanguage}");

                    // 日本語か英語のみサポート
                    var languageCode = systemLanguage == "ja" ? "ja" : "en";

                    // 現在の言語を保存
                    _currentLanguage = languageCode;

                    SetLanguage(languageCode);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in LoadLanguageSetting: {ex.Message}");
                // エラーが発生した場合はデフォルト言語（英語）を設定
                _currentLanguage = "en";
                SetLanguage("en");
            }
        }

        private void SaveLanguageSetting(string languageCode)
        {
            try
            {
                Debug.WriteLine($"Saving language setting: {languageCode} to {_configPath}");
                var config = new AppConfig { Language = languageCode };
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
                Debug.WriteLine("Language setting saved successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving language setting: {ex.Message}");
                // 保存に失敗した場合は無視
            }
        }

        private class AppConfig
        {
            public string Language { get; set; }
        }
    }
}