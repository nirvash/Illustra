using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace Illustra.Helpers
{
    public class AppSettings
    {
        // ウィンドウサイズと位置
        public double WindowWidth { get; set; } = 900;
        public double WindowHeight { get; set; } = 600;
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public WindowState WindowState { get; set; } = WindowState.Normal;

        // 最後に開いていたフォルダ
        public string LastFolderPath { get; set; } = string.Empty;

        // サムネイルサイズ
        public int ThumbnailSize { get; set; } = 120;

        // 最後に選択したファイル
        public string LastSelectedFilePath { get; set; } = string.Empty;

        // スクロール設定
        public double MouseWheelMultiplier { get; set; } = 1.0;

        // ビューア設定
        public bool SaveViewerState { get; set; } = true;

        // ソート順設定
        public bool SortByDate { get; set; } = true;
        public bool SortAscending { get; set; } = true;

        // スプリッター位置設定
        public double FavoriteFoldersHeight { get; set; } = 0;
        public double MainSplitterPosition { get; set; } = 0;
        public double PropertySplitterPosition { get; set; } = 0;

        // お気に入りフォルダ
        public ObservableCollection<string> FavoriteFolders { get; set; } = new ObservableCollection<string>();

        // アプリケーションの言語設定
        public string Language { get; set; } = CultureInfo.CurrentUICulture.Name;

        // プロパティパネルのフォルダパス折りたたみ状態
        public bool FolderPathExpanded { get; set; } = false;

        // プロパティパネルの詳細情報の折りたたみ状態
        public bool DetailsExpanded { get; set; } = false;

        // プロパティパネルのStable Diffusion情報の折りたたみ状態
        public bool StableDiffusionExpanded { get; set; } = false;
    }

    public static class SettingsHelper
    {
        private static AppSettings? _currentSettings;

        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Illustra",
            "settings.json");

        // デフォルトの翻訳を保持
        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["ContinueIterationPrompt"] = "続行: 反復処理を続行しますか?"
            },
            ["en"] = new Dictionary<string, string>
            {
                ["ContinueIterationPrompt"] = "Continue: Continue iteration?"
            }
        };

        // 翻訳されたテキストを取得する
        public static string GetTranslation(string key, string defaultValue = "")
        {
            var settings = GetSettings();
            var cultureName = settings.Language;

            // 指定された言語の翻訳が存在する場合
            if (Translations.TryGetValue(cultureName, out var translations) &&
                translations.TryGetValue(key, out var value))
            {
                return value;
            }

            // 英語をフォールバック言語として使用
            if (cultureName != "en" &&
                Translations.TryGetValue("en", out var enTranslations) &&
                enTranslations.TryGetValue(key, out var enValue))
            {
                return enValue;
            }

            return defaultValue;
        }

        // アプリケーション設定を取得
        public static AppSettings GetSettings()
        {
            // 既に読み込まれている場合はそれを返す
            if (_currentSettings != null)
                return _currentSettings;

            try
            {
                // 設定ファイルが存在すれば読み込み
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    _currentSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                    if (_currentSettings != null)
                        return _currentSettings;
                }
            }
            catch (Exception ex)
            {
                // エラー発生時はログ出力など
                System.Diagnostics.Debug.WriteLine($"設定の読み込み中にエラーが発生しました: {ex.Message}");
            }

            // 設定ファイルがないか読み込み失敗した場合はデフォルト設定を返す
            _currentSettings = new AppSettings();
            return _currentSettings;
        }

        // アプリケーション設定を保存
        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                _currentSettings = settings;

                // ディレクトリが存在しない場合は作成
                string? directory = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 設定をJSON形式で保存
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                // エラー発生時はログ出力など
                System.Diagnostics.Debug.WriteLine($"設定の保存中にエラーが発生しました: {ex.Message}");
            }
        }

        // 言語を変更
        public static void SetLanguage(string language)
        {
            var settings = GetSettings();
            settings.Language = language;
            SaveSettings(settings);

            // UI Culture を変更
            var culture = new CultureInfo(language);
            CultureInfo.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
    }
}
