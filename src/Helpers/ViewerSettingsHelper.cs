using System.IO;
using Newtonsoft.Json;

namespace Illustra.Helpers
{
    public enum FileDeleteMode
    {
        Permanent, // 完全削除
        RecycleBin // ごみ箱に削除
    }

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
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonConvert.DeserializeObject<ViewerSettings>(json) ?? new ViewerSettings();
                    return settings;
                }
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
        public bool VisiblePropertyPanel { get; set; } = true;
        public double NormalPropertyColumnWidth { get; set; } = 250; // 通常ウィンドウ時のプロパティパネル幅
        public double FullScreenPropertyColumnWidth { get; set; } = 250; // 全画面表示時のプロパティパネル幅
        public double SlideshowIntervalSeconds { get; set; } = 2.0; // スライドショーの間隔（秒）

        // 表示項目の設定
        public bool ShowFileName { get; set; } = true;
        public bool ShowRating { get; set; } = true;
        public bool ShowDetails { get; set; } = true;
        public bool ShowStableDiffusion { get; set; } = true;
        public bool ShowComment { get; set; } = true;

        // フォントサイズの設定
        public double PropertyPanelFontSize { get; set; } = 12.0; // フォントサイズ

        // ファイル操作の設定
        public FileDeleteMode DeleteMode { get; set; } = FileDeleteMode.RecycleBin; // デフォルトはごみ箱に削除

        // WebP動画の表示設定
        public bool FitSmallAnimationToScreen { get; set; } = false; // デフォルトは1:1表示
        // Video Player Settings
        public double VideoVolume { get; set; } = 0.5; // Default volume
    }
}
