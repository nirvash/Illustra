using System.IO;
using System.Text.Json;

namespace Illustra.Models
{
    public class ImageGenerationSettingsModel
    {
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Illustra",
            "ImageGenerationSettings.json");

        public string ServerUrl { get; set; } = "http://127.0.0.1:7860";
        public string ReforgePath { get; set; } = string.Empty;
        public string LastUsedTags { get; set; } = string.Empty;

        public static ImageGenerationSettingsModel Load()
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<ImageGenerationSettingsModel>(json)
                       ?? new ImageGenerationSettingsModel();
            }
            return new ImageGenerationSettingsModel();
        }

        public void Save()
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsFilePath, json);
        }
    }
}
