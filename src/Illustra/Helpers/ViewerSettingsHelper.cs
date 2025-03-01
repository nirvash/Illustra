using System.IO;
using System.Windows;
using Newtonsoft.Json;

namespace Illustra.Helpers
{
    public static class ViewerSettingsHelper
    {
        // 設定ファイルのパスを固定値で設定
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Illustra", "viewerSettings.json");

        public static ViewerSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Loading viewer settings from: {SettingsFilePath}");
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonConvert.DeserializeObject<ViewerSettings>(json) ?? new ViewerSettings();
                    System.Diagnostics.Debug.WriteLine($"Loaded settings - Width: {settings.Width}, Height: {settings.Height}, IsFullScreen: {settings.IsFullScreen}");
                    return settings;
                }
                System.Diagnostics.Debug.WriteLine($"Settings file not found at: {SettingsFilePath}, using defaults");
                return new ViewerSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading viewer settings: {ex.Message}");
                return new ViewerSettings();
            }
        }

        public static void SaveSettings(ViewerSettings settings)
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    System.Diagnostics.Debug.WriteLine($"Creating settings directory: {directory}");
                    Directory.CreateDirectory(directory);
                }

                System.Diagnostics.Debug.WriteLine($"Saving viewer settings to: {SettingsFilePath}");
                System.Diagnostics.Debug.WriteLine($"Saving settings - Width: {settings.Width}, Height: {settings.Height}, IsFullScreen: {settings.IsFullScreen}");

                // 設定をファイルに保存
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving viewer settings: {ex.Message}");
            }
        }
    }

    public class ViewerSettings
    {
        public double Left { get; set; } = double.NaN;
        public double Top { get; set; } = double.NaN;
        public double Width { get; set; } = 800;
        public double Height { get; set; } = 600;
        public bool IsFullScreen { get; set; } = false;
    }
}
