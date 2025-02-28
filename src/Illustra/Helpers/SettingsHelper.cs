using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Illustra.Helpers
{
    public class AppSettings
    {
        // ウィンドウサイズと位置
        public double WindowWidth { get; set; } = 800;
        public double WindowHeight { get; set; } = 450;
        public double WindowLeft { get; set; } = 100;
        public double WindowTop { get; set; } = 100;
        public WindowState WindowState { get; set; } = WindowState.Normal;

        // 最後に開いていたフォルダ
        public string LastFolderPath { get; set; } = string.Empty;

        // サムネイルサイズ
        public int ThumbnailSize { get; set; } = 120;

        // 他の設定を必要に応じて追加
    }

    public static class SettingsHelper
    {
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Illustra",
            "settings.json");

        // 現在のアプリケーション設定を保持
        private static AppSettings _currentSettings;

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
                string directory = Path.GetDirectoryName(SettingsFilePath);
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
    }
}
