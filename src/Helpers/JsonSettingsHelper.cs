using System;
using System.IO;
using System.Text.Json;

namespace Illustra.Helpers
{
    public static class JsonSettingsHelper
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Illustra");

        public static T LoadSettings<T>(string filename) where T : new()
        {
            try
            {
                string filePath = Path.Combine(SettingsDirectory, filename);
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<T>(json) ?? new T();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"設定の読み込み中にエラーが発生しました: {ex.Message}");
            }

            return new T();
        }

        public static void SaveSettings<T>(T settings, string filename)
        {
            try
            {
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }

                string filePath = Path.Combine(SettingsDirectory, filename);
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"設定の保存中にエラーが発生しました: {ex.Message}");
            }
        }
    }
}
