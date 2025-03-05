using System.IO;

namespace Illustra.Helpers
{
    public static class FileHelper
    {
        public static bool IsImageFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png" ||
                   extension == ".gif" || extension == ".bmp" || extension == ".webp";
        }

        public static string[] SupportedExtensions => new[]
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"
        };
    }
}
