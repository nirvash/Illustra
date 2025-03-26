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
    }
}
