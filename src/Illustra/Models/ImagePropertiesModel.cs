using System.IO;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace Illustra.Models
{
    public class ImagePropertiesModel
    {
        // ファイル情報
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FileSizeFormatted => FormatFileSize(FileSizeBytes);
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        // 画像情報
        public int Width { get; set; }
        public int Height { get; set; }
        public string Resolution { get; set; } = string.Empty;
        public string ImageFormat { get; set; } = string.Empty;
        public string ColorDepth { get; set; } = string.Empty;

        // Exif情報
        public string CameraModel { get; set; } = string.Empty;
        public string ExposureTime { get; set; } = string.Empty;
        public string FNumber { get; set; } = string.Empty;
        public string ISOSpeed { get; set; } = string.Empty;
        public DateTime? DateTaken { get; set; }

        /// <summary>
        /// 指定したファイルパスから画像プロパティを非同期的に読み込みます
        /// </summary>
        public static async Task<ImagePropertiesModel> LoadFromFileAsync(string filePath)
        {
            var properties = new ImagePropertiesModel();

            try
            {
                if (!File.Exists(filePath))
                    return properties;

                properties.FilePath = filePath;
                properties.FileName = Path.GetFileName(filePath);

                var fileInfo = new FileInfo(filePath);
                properties.FileSizeBytes = fileInfo.Length;
                properties.CreatedDate = fileInfo.CreationTime;
                properties.ModifiedDate = fileInfo.LastWriteTime;

                await Task.Run(() =>
                {
                    try
                    {
                        using (var stream = File.OpenRead(filePath))
                        using (var skStream = new SKManagedStream(stream))
                        using (var codec = SKCodec.Create(skStream))
                        {
                            if (codec != null)
                            {
                                var info = codec.Info;
                                properties.Width = info.Width;
                                properties.Height = info.Height;
                                properties.ImageFormat = codec.EncodedFormat.ToString();
                                properties.Resolution = $"{info.Width} x {info.Height} ピクセル";
                                properties.ColorDepth = GetColorDepth(codec);
                            }
                        }

                        // Exif情報の読み取り
                        ReadExifData(filePath, properties);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"画像プロパティ読み取りエラー: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"プロパティ読み込みエラー: {ex.Message}");
            }

            return properties;
        }

        private static string GetColorDepth(SKCodec codec)
        {
            try
            {
                switch (codec.EncodedFormat)
                {
                    case SKEncodedImageFormat.Jpeg:
                    case SKEncodedImageFormat.Png:
                    case SKEncodedImageFormat.Webp:
                        return "24bit"; // 通常はRGB 8ビット/チャンネル
                    case SKEncodedImageFormat.Gif:
                        return "8bit"; // GIFは通常8ビット
                    default:
                        return "不明";
                }
            }
            catch
            {
                return "不明";
            }
        }

        private static void ReadExifData(string filePath, ImagePropertiesModel properties)
        {
            try
            {
                // BitmapSourceを使用してExifデータを取得
                BitmapFrame frame = BitmapFrame.Create(new Uri(filePath), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                BitmapMetadata? metadata = frame.Metadata as BitmapMetadata;

                if (metadata != null)
                {
                    // カメラモデル
                    if (metadata.ContainsQuery("System.Photo.CameraManufacturer") && metadata.ContainsQuery("System.Photo.CameraModel"))
                    {
                        var manufacturer = metadata.GetQuery("System.Photo.CameraManufacturer") as string;
                        var model = metadata.GetQuery("System.Photo.CameraModel") as string;

                        if (!string.IsNullOrEmpty(manufacturer) && !string.IsNullOrEmpty(model))
                            properties.CameraModel = $"{manufacturer} {model}";
                        else if (!string.IsNullOrEmpty(model))
                            properties.CameraModel = model;
                    }

                    // 撮影日時
                    if (metadata.ContainsQuery("System.Photo.DateTaken"))
                    {
                        if (metadata.GetQuery("System.Photo.DateTaken") is DateTime dateTaken)
                        {
                            properties.DateTaken = dateTaken;
                        }
                    }

                    // 露出時間
                    if (metadata.ContainsQuery("System.Photo.ExposureTime"))
                    {
                        var exposureTime = metadata.GetQuery("System.Photo.ExposureTime");
                        if (exposureTime != null)
                        {
                            properties.ExposureTime = $"{exposureTime}";
                        }
                    }

                    // F値
                    if (metadata.ContainsQuery("System.Photo.FNumber"))
                    {
                        var fNumber = metadata.GetQuery("System.Photo.FNumber");
                        if (fNumber != null)
                        {
                            properties.FNumber = $"F{fNumber}";
                        }
                    }

                    // ISO感度
                    if (metadata.ContainsQuery("System.Photo.ISOSpeed"))
                    {
                        var iso = metadata.GetQuery("System.Photo.ISOSpeed");
                        if (iso != null)
                        {
                            properties.ISOSpeed = $"ISO {iso}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exif読み取りエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ファイルサイズを読みやすい形式でフォーマットします
        /// </summary>
        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }
}
