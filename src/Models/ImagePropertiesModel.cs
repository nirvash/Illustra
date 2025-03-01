using System.IO;
using System.ComponentModel;
using SkiaSharp;
using System.Text;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Illustra.Models
{
    public class ImagePropertiesModel : INotifyPropertyChanged
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
        public string FNumberAndISO
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(FNumber)) parts.Add(FNumber);
                if (!string.IsNullOrEmpty(ISOSpeed)) parts.Add(ISOSpeed);
                return string.Join(" / ", parts);
            }
        }
        public DateTime? DateTaken { get; set; }
        private string _userComment = string.Empty;
        public string UserComment
        {
            get => _userComment;
            set
            {
                if (_userComment != value)
                {
                    _userComment = value;
                    OnPropertyChanged(nameof(UserComment));
                }
            }
        }

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
                var directories = ImageMetadataReader.ReadMetadata(filePath);
                var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();

                if (exif != null)
                {
                    // ユーザーコメント
                    properties.UserComment = exif.GetDescription(ExifDirectoryBase.TagUserComment) ?? string.Empty;

                    // 撮影日時
                    exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime dateTime);
                    properties.DateTaken = dateTime;

                    // ISO感度
                    exif.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out int iso);
                    properties.ISOSpeed = $"ISO {iso}";

                    // 露出時間
                    exif.TryGetRational(ExifDirectoryBase.TagExposureTime, out Rational exposure);
                    // 1秒以上の場合は小数点形式、1秒未満の場合は分数形式で表示
                    if (exposure.ToDouble() >= 1.0)
                    {
                        properties.ExposureTime = $"{exposure.ToDouble():0.#}秒";
                    }
                    else
                    {
                        properties.ExposureTime = $"1/{(1.0 / exposure.ToDouble()):0}秒";
                    }

                    // F値
                    exif.TryGetRational(ExifDirectoryBase.TagFNumber, out Rational fNumber);
                    properties.FNumber = $"F{fNumber.ToDouble():0.#}";
                }

                if (exifIfd0 != null)
                {
                    // カメラ情報
                    string make = string.Empty;
                    string model = string.Empty;

                    try { make = exifIfd0.GetString(ExifDirectoryBase.TagMake) ?? string.Empty; } catch (MetadataException) { }
                    try { model = exifIfd0.GetString(ExifDirectoryBase.TagModel) ?? string.Empty; } catch (MetadataException) { }

                    if (!string.IsNullOrEmpty(make) && !string.IsNullOrEmpty(model))
                    {
                        properties.CameraModel = $"{make} {model}";
                    }
                    else if (!string.IsNullOrEmpty(model))
                    {
                        properties.CameraModel = model;
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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
