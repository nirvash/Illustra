using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SkiaSharp;
using Illustra.Helpers;
using StableDiffusionTools;
using System.Diagnostics;
using System.Text;

namespace Illustra.Models
{
    public class ImagePropertiesModel : INotifyPropertyChanged
    {
        private string _filePath = string.Empty;
        private string _fileName = string.Empty;
        private long _fileSize;
        private string _dimensions = string.Empty;
        private string _createdDate = string.Empty;
        private string _modifiedDate = string.Empty;
        private int _rating;
        private BitmapSource? _preview;
        private DateTime _creationTime;
        private DateTime _lastModified;
        private string _fileType = string.Empty;
        private string _folderPath = string.Empty;
        private int _width;
        private int _height;
        private string _imageFormat = string.Empty;
        private string _colorDepth = string.Empty;
        private string _userComment = string.Empty;
        private DateTime _dateTaken;
        private string _isoSpeed = string.Empty;
        private string _exposureTime = string.Empty;
        private string _fNumber = string.Empty;
        private string _cameraModel = string.Empty;
        private bool _folderPathExpanded = false;
        private string _folderPathShort = string.Empty;
        private StableDiffusionParser.ParseResult? _stableDiffusionResult;
        private bool _stableDiffusionExpanded = false;

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged(nameof(FilePath));
                }
            }
        }

        public string FileName
        {
            get => _fileName;
            set
            {
                if (_fileName != value)
                {
                    _fileName = value;
                    OnPropertyChanged(nameof(FileName));
                }
            }
        }

        public long FileSize
        {
            get => _fileSize;
            set
            {
                if (_fileSize != value)
                {
                    _fileSize = value;
                    OnPropertyChanged(nameof(FileSize));
                    OnPropertyChanged(nameof(FileSizeFormatted));
                }
            }
        }

        public string FileSizeFormatted
        {
            get => FormatFileSize(_fileSize);
        }

        public string Dimensions
        {
            get => _dimensions;
            set
            {
                if (_dimensions != value)
                {
                    _dimensions = value;
                    OnPropertyChanged(nameof(Dimensions));
                }
            }
        }

        public string Resolution
        {
            get => _dimensions;
            set
            {
                if (_dimensions != value)
                {
                    _dimensions = value;
                    OnPropertyChanged(nameof(Resolution));
                }
            }
        }

        public string CreatedDate
        {
            get => _createdDate;
            set
            {
                if (_createdDate != value)
                {
                    _createdDate = value;
                    OnPropertyChanged(nameof(CreatedDate));
                }
            }
        }

        public string ModifiedDate
        {
            get => _modifiedDate;
            set
            {
                if (_modifiedDate != value)
                {
                    _modifiedDate = value;
                    OnPropertyChanged(nameof(ModifiedDate));
                }
            }
        }

        public int Rating
        {
            get => _rating;
            set
            {
                if (_rating != value)
                {
                    _rating = value;
                    OnPropertyChanged(nameof(Rating));
                }
            }
        }

        public BitmapSource? Preview
        {
            get => _preview;
            set
            {
                if (_preview != value)
                {
                    _preview = value;
                    OnPropertyChanged(nameof(Preview));
                }
            }
        }

        public DateTime CreationTime
        {
            get => _creationTime;
            set
            {
                if (_creationTime != value)
                {
                    _creationTime = value;
                    CreatedDate = value.ToString("yyyy/MM/dd HH:mm:ss");
                    OnPropertyChanged(nameof(CreationTime));
                }
            }
        }

        public DateTime LastModified
        {
            get => _lastModified;
            set
            {
                if (_lastModified != value)
                {
                    _lastModified = value;
                    ModifiedDate = value.ToString("yyyy/MM/dd HH:mm:ss");
                    OnPropertyChanged(nameof(LastModified));
                }
            }
        }

        public string FileType
        {
            get => _fileType;
            set
            {
                if (_fileType != value)
                {
                    _fileType = value;
                    OnPropertyChanged(nameof(FileType));
                }
            }
        }

        public string FolderPath
        {
            get => _folderPath;
            set
            {
                if (_folderPath != value)
                {
                    _folderPath = value;
                    UpdateFolderPathShort();
                    OnPropertyChanged(nameof(FolderPath));
                }
            }
        }

        public string FolderPathShort
        {
            get => _folderPathShort;
            private set
            {
                if (_folderPathShort != value)
                {
                    _folderPathShort = value;
                    OnPropertyChanged(nameof(FolderPathShort));
                }
            }
        }

        public bool FolderPathExpanded
        {
            get => _folderPathExpanded;
            set
            {
                if (_folderPathExpanded != value)
                {
                    _folderPathExpanded = value;
                    OnPropertyChanged(nameof(FolderPathExpanded));
                    SaveExpandedState();
                }
            }
        }

        public bool StableDiffusionExpanded
        {
            get => _stableDiffusionExpanded;
            set
            {
                if (_stableDiffusionExpanded != value)
                {
                    _stableDiffusionExpanded = value;
                    OnPropertyChanged(nameof(StableDiffusionExpanded));
                    SaveExpandedState();
                }
            }
        }

        // 追加プロパティ
        public int Width
        {
            get => _width;
            set
            {
                if (_width != value)
                {
                    _width = value;
                    OnPropertyChanged(nameof(Width));
                }
            }
        }

        public int Height
        {
            get => _height;
            set
            {
                if (_height != value)
                {
                    _height = value;
                    OnPropertyChanged(nameof(Height));
                }
            }
        }

        public string ImageFormat
        {
            get => _imageFormat;
            set
            {
                if (_imageFormat != value)
                {
                    _imageFormat = value;
                    OnPropertyChanged(nameof(ImageFormat));
                }
            }
        }

        public string ColorDepth
        {
            get => _colorDepth;
            set
            {
                if (_colorDepth != value)
                {
                    _colorDepth = value;
                    OnPropertyChanged(nameof(ColorDepth));
                }
            }
        }

        public string UserComment
        {
            get => _userComment;
            set
            {
                if (_userComment != value)
                {
                    _userComment = value;
                    OnPropertyChanged(nameof(UserComment));

                    // UserCommentが更新されたらStable Diffusion解析を試みる
                    if (!string.IsNullOrEmpty(value))
                    {
                        try
                        {
                            StableDiffusionResult = StableDiffusionParser.Parse(value);
                        }
                        catch
                        {
                            StableDiffusionResult = null;
                        }
                    }
                    else
                    {
                        StableDiffusionResult = null;
                    }
                }
            }
        }

        public StableDiffusionParser.ParseResult? StableDiffusionResult
        {
            get => _stableDiffusionResult;
            private set
            {
                if (_stableDiffusionResult != value)
                {
                    _stableDiffusionResult = value;
                    OnPropertyChanged(nameof(StableDiffusionResult));
                    OnPropertyChanged(nameof(HasStableDiffusionData));
                }
            }
        }

        public bool HasStableDiffusionData => StableDiffusionResult != null;

        public DateTime DateTaken
        {
            get => _dateTaken;
            set
            {
                if (_dateTaken != value)
                {
                    _dateTaken = value;
                    OnPropertyChanged(nameof(DateTaken));
                }
            }
        }

        public string ISOSpeed
        {
            get => _isoSpeed;
            set
            {
                if (_isoSpeed != value)
                {
                    _isoSpeed = value;
                    OnPropertyChanged(nameof(ISOSpeed));
                }
            }
        }

        public string ExposureTime
        {
            get => _exposureTime;
            set
            {
                if (_exposureTime != value)
                {
                    _exposureTime = value;
                    OnPropertyChanged(nameof(ExposureTime));
                }
            }
        }

        public string FNumber
        {
            get => _fNumber;
            set
            {
                if (_fNumber != value)
                {
                    _fNumber = value;
                    OnPropertyChanged(nameof(FNumber));
                }
            }
        }

        public string CameraModel
        {
            get => _cameraModel;
            set
            {
                if (_cameraModel != value)
                {
                    _cameraModel = value;
                    OnPropertyChanged(nameof(CameraModel));
                }
            }
        }

        // FileSizeBytes は FileSize と同じものとして扱います
        public long FileSizeBytes
        {
            get => _fileSize;
            set => FileSize = value;
        }

        public ImagePropertiesModel()
        {
            LoadExpandedState();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string? propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Clear()
        {
            FilePath = string.Empty;
            FileName = string.Empty;
            FileSize = 0;
            Dimensions = string.Empty;
            CreatedDate = string.Empty;
            ModifiedDate = string.Empty;
            Rating = 0;
            Preview = null;
            FileType = string.Empty;
            Width = 0;
            Height = 0;
            ImageFormat = string.Empty;
            ColorDepth = string.Empty;
            UserComment = string.Empty;
            DateTaken = DateTime.MinValue;
            ISOSpeed = string.Empty;
            ExposureTime = string.Empty;
            FNumber = string.Empty;
            CameraModel = string.Empty;
            FolderPath = string.Empty;
            FolderPathShort = string.Empty;
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
                properties.CreationTime = fileInfo.CreationTime;
                properties.LastModified = fileInfo.LastWriteTime;

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
        static string DetectAndDecodeUtf16(byte[] data)
        {
            Encoding encoding;

            // 🔹 BOM でエンディアンを判定
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            {
                encoding = Encoding.BigEndianUnicode;
                data = data.Skip(2).ToArray(); // BOM を除去
            }
            else if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            {
                encoding = Encoding.Unicode; // UTF-16LE
                data = data.Skip(2).ToArray(); // BOM を除去
            }
            else
            {
                // BOM がない場合、Exif の仕様に従い UTF-16BE とみなす
                encoding = Encoding.BigEndianUnicode;
            }

            return encoding.GetString(data);
        }

        private static void ReadExifData(string filePath, ImagePropertiesModel properties)
        {
            try
            {
                // PNGファイルの場合は、Parametersチャンクを確認
                if (Path.GetExtension(filePath).Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    var pngParameters = PngMetadataReader.ReadTextChunk(filePath, "parameters");
                    if (!string.IsNullOrEmpty(pngParameters))
                    {
                        properties.UserComment = pngParameters;
                        return; // PNG形式でプロンプト情報が見つかった場合は、Exif情報は不要
                    }
                }

                // Exif情報を読み取る
                var directories = ImageMetadataReader.ReadMetadata(filePath);
                var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();

                if (exif != null)
                {
                    // ユーザーコメント
                    try
                    {
                        var bytes = exif.GetByteArray(ExifDirectoryBase.TagUserComment);
                        if (bytes != null && bytes.Length > 8)
                        {
                            // 最初の8バイトがエンコーディング識別子
                            var encodingStr = System.Text.Encoding.ASCII.GetString(bytes.Take(8).ToArray());
                            if (encodingStr.StartsWith("ASCII") || encodingStr.Equals("\0\0\0\0\0\0\0\0"))
                            {
                                // ASCIIエンコーディング情報がある場合はUTF-8としてデコード
                                properties.UserComment = System.Text.Encoding.UTF8.GetString(bytes.Skip(8).ToArray());
                            }
                            else if (encodingStr.StartsWith("UNICODE"))
                            {
                                // UNICODEエンコーディング情報がある場合はUTF-16としてデコード(BOMなしの場合はUTF-16BE)
                                properties.UserComment = DetectAndDecodeUtf16(bytes.Skip(8).ToArray());
                            }
                            else
                            {
                                // その他の場合は既存の方式で取得
                                properties.UserComment = exif.GetDescription(ExifDirectoryBase.TagUserComment) ?? string.Empty;
                            }
                        }
                        else
                        {
                            // バイトデータが取得できない場合は既存の方式で取得
                            properties.UserComment = exif.GetDescription(ExifDirectoryBase.TagUserComment) ?? string.Empty;
                        }
                    }
                    catch (Exception)
                    {
                        properties.UserComment = exif.GetDescription(ExifDirectoryBase.TagUserComment) ?? string.Empty;
                    }

                    // 撮影日時
                    exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime dateTime);
                    properties.DateTaken = dateTime;

                    // ISO感度
                    if (exif.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out int iso) && iso > 0)
                    {
                        properties.ISOSpeed = $"ISO {iso}";
                    }

                    // 露出時間
                    if (exif.TryGetRational(ExifDirectoryBase.TagExposureTime, out Rational exposure))
                    {
                        double exposureValue = exposure.ToDouble();
                        if (exposureValue > 0 && !double.IsInfinity(exposureValue))
                        {
                            // 1秒以上の場合は小数点形式、1秒未満の場合は分数形式で表示
                            if (exposureValue >= 1.0)
                            {
                                properties.ExposureTime = $"{exposureValue:0.#}秒";
                            }
                            else
                            {
                                properties.ExposureTime = $"1/{(1.0 / exposureValue):0}秒";
                            }
                        }
                    }

                    // F値
                    if (exif.TryGetRational(ExifDirectoryBase.TagFNumber, out Rational fNumber))
                    {
                        double fValue = fNumber.ToDouble();
                        if (fValue > 0 && !double.IsInfinity(fValue))
                        {
                            properties.FNumber = $"F{fValue:0.#}";
                        }
                    }
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

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            double number = bytes;

            while (number > 1024 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }

            return $"{number:0.##} {suffixes[counter]}";
        }

        private static string GetColorDepth(SKCodec codec)
        {
            try
            {
                var info = codec.Info;

                // カラータイプに基づいて推測
                switch (info.ColorType)
                {
                    case SKColorType.Rgba8888:
                    case SKColorType.Rgb888x:
                        return "24/32 bit";
                    case SKColorType.Rgb565:
                        return "16 bit";
                    case SKColorType.Gray8:
                        return "8 bit";
                    default:
                        return info.ColorType.ToString();
                }
            }
            catch
            {
                return "Unknown";
            }
        }

        private void UpdateFolderPathShort()
        {
            if (string.IsNullOrEmpty(_folderPath))
            {
                FolderPathShort = string.Empty;
                return;
            }

            try
            {
                var parts = _folderPath.Split(Path.DirectorySeparatorChar);
                if (parts.Length <= 2)
                {
                    FolderPathShort = _folderPath;
                }
                else
                {
                    FolderPathShort = $"{parts[0]}{Path.DirectorySeparatorChar}...{Path.DirectorySeparatorChar}{parts[^1]}";
                }
            }
            catch
            {
                FolderPathShort = _folderPath;
            }
        }

        private void SaveExpandedState()
        {
            var settings = SettingsHelper.GetSettings();
            settings.FolderPathExpanded = _folderPathExpanded;
            settings.StableDiffusionExpanded = _stableDiffusionExpanded;
            SettingsHelper.SaveSettings(settings);
        }

        private void LoadExpandedState()
        {
            var settings = SettingsHelper.GetSettings();
            _folderPathExpanded = settings.FolderPathExpanded;
            _stableDiffusionExpanded = settings.StableDiffusionExpanded;
            OnPropertyChanged(nameof(FolderPathExpanded));
            OnPropertyChanged(nameof(StableDiffusionExpanded));
        }
    }
}
