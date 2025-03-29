using System.IO;
using System.Linq;

namespace Illustra.Helpers
{
    public static class FileHelper
    {
        public static bool IsImageFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return SupportedImageExtensions.Contains(extension);
        }

        public static bool IsVideoFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return SupportedVideoExtensions.Contains(extension);
        }

        public static bool IsMediaFile(string filePath)
        {
            return IsImageFile(filePath) || IsVideoFile(filePath);
        }

        public static string[] SupportedImageExtensions => new[]
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"
        };

        public static string[] SupportedVideoExtensions => new[]
        {
            ".mp4"
        };

        public static string[] SupportedExtensions => SupportedImageExtensions.Concat(SupportedVideoExtensions).ToArray();

        public static bool IsValidFileName(string fileName)
        {
            // 禁止文字を取得
            char[] invalidChars = Path.GetInvalidFileNameChars();

            // 禁止文字が含まれているかチェック
            if (fileName.IndexOfAny(invalidChars) >= 0)
            {
                return false;
            }

            // 予約語のチェック（大文字小文字を区別せず）
            string[] reservedNames = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                                   "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName).ToUpper();

            if (reservedNames.Contains(nameWithoutExt))
            {
                return false;
            }

            string GetFileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(GetFileNameWithoutExtension))
            {
                return false;
            }

            // ファイル名が空じゃないことをチェック
            return !string.IsNullOrWhiteSpace(fileName);
        }

        /// <summary>
        /// 指定されたベースパスをもとにユニークなフォルダ名を生成します。
        /// </summary>
        /// <param name="basePath">ベースとなるフォルダパス</param>
        /// <returns>ユニークなフォルダパス</returns>
        public static string GenerateUniqueFolderPath(string basePath)
        {
            string uniquePath = basePath;
            int counter = 1;

            while (Directory.Exists(uniquePath))
            {
                uniquePath = $"{basePath} ({counter++})";
            }

            return uniquePath;
        }

        /// <summary>
        /// バイト配列の先頭から画像形式を判定し、対応する拡張子を返します。
        /// </summary>
        /// <param name="bytes">画像データのバイト配列</param>
        /// <returns>判定された拡張子（例: ".jpg"）、不明な場合はnull</returns>
        public static string? GetImageExtensionFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) // 少なくとも数バイトは必要
                return null;

            // JPEG (FF D8 FF)
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return ".jpg";

            // PNG (89 50 4E 47)
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return ".png";

            // GIF (47 49 46 38)
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
                return ".gif";

            // BMP (42 4D)
            if (bytes[0] == 0x42 && bytes[1] == 0x4D)
                return ".bmp";

            // WebP (RIFF ???? WEBP)
            if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return ".webp";

            // MP4 (ftyp signature at offset 4)
            if (bytes.Length >= 8 &&
                bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70) // "ftyp"
                return ".mp4";

            // 不明な形式
            return null;
        }

    }
}
