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
using DryIoc.ImTools;
using TagLib; // Changed from MediaInfo

namespace Illustra.Models
{
    public class ImagePropertiesModel : INotifyPropertyChanged
    {
        private string _filePath = string.Empty;
        private string _fileName = string.Empty;
        private long _fileSize;
        // private string _dimensions = string.Empty; // Removed as it's calculated by Dimensions property
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
        private bool _isVideo;
        private TimeSpan _duration;
        private bool _stableDiffusionExpanded = false;
        public string DurationFormatted => _duration.ToString(@"hh\:mm\:ss");

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
            get
            {
                // 多言語対応: "ピクセル" 部分をリソースから取得（Properties.Resources.PixelUnitLabel → Application.Current.TryFindResource）
                var pixelLabel = System.Windows.Application.Current.TryFindResource("String_Unit_Pixel") as string ?? "pixels";
                return $"{_width} x {_height} {pixelLabel}";
            }
            // Setter は不要。値は Width/Height から計算されるため。
        }

        public string Resolution
        {
            get => Dimensions; // Return the calculated Dimensions property
            // Setter は不要。値は Dimensions プロパティから取得されるため。
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
                    OnPropertyChanged(nameof(Dimensions)); // Notify Dimensions change
                    OnPropertyChanged(nameof(Resolution)); // Notify Resolution change
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
                    OnPropertyChanged(nameof(Dimensions)); // Notify Dimensions change
                    OnPropertyChanged(nameof(Resolution)); // Notify Resolution change
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
                }
            }
        }

        /// <summary>
        /// Stable Diffusionの解析結果を設定する
        /// </summary>
        /// <param name="metadata">メタデータ</param>
        public void SetStableDiffusionData(StableDiffusionMetadata metadata)
        {
            if (metadata == null || !metadata.ParseSuccess)
            {
                StableDiffusionResult = null;
                return;
            }

            try
            {
                // 新しい変換ヘルパーを使用してレガシーフォーマットに変換
                var legacyResult = StableDiffusionMetadataManager.ConvertToLegacyParseResult(metadata);
                StableDiffusionResult = legacyResult;
            }
            catch
            {
                StableDiffusionResult = null;
            }
        }

        /// <summary>
        /// UserCommentからStable Diffusionデータを解析する
        /// </summary>
        public void ParseStableDiffusionFromUserComment()
        {
            if (string.IsNullOrEmpty(UserComment))
            {
                StableDiffusionResult = null;
                return;
            }

            try
            {
                // 新しいパーサーシステムを使用してメタデータを解析
                var metadata = StableDiffusionMetadataManager.ParseMetadataText(UserComment);
                if (metadata.ParseSuccess)
                {
                    SetStableDiffusionData(metadata);
                }
                else
                {
                    // 従来のパーサーを使用（後方互換性のため）
                    StableDiffusionResult = StableDiffusionParser.Parse(UserComment);
                }
            }
            catch
            {
                StableDiffusionResult = null;
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
            // Dimensions is calculated, no need to clear
            CreatedDate = string.Empty;
            ModifiedDate = string.Empty;
            Rating = 0;
            Preview = null;
            FileType = string.Empty;
            Width = 0;
            Height = 0;
            IsVideo = false; // Clear video flag
            Duration = TimeSpan.Zero; // Clear duration
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
            StableDiffusionResult = null; // Clear SD data
        }

        /// <summary>
        /// 指定したファイルパスから画像プロパティを非同期的に読み込みます
        /// </summary>
        public static async Task<ImagePropertiesModel> LoadFromFileAsync(string filePath)
        {
            var properties = new ImagePropertiesModel();

            try
            {
                if (!System.IO.File.Exists(filePath)) // Use fully qualified name
                    return properties;

                properties.FilePath = filePath;
                properties.FileName = Path.GetFileName(filePath);
                properties.FolderPath = Path.GetDirectoryName(filePath) ?? string.Empty; // Handle null case

                var fileInfo = new FileInfo(filePath);
                properties.FileSizeBytes = fileInfo.Length; // Use setter for FileSize
                properties.CreationTime = fileInfo.CreationTime; // Use setter
                properties.LastModified = fileInfo.LastWriteTime; // Use setter
                properties.FileType = Path.GetExtension(filePath).ToLowerInvariant(); // Use setter

                // FileHelper を使用してファイルタイプに基づいて処理を分岐
                if (FileHelper.IsVideoFile(filePath))
                {
                    properties.IsVideo = true; // Use setter
                    await Task.Run(() => // TagLib processing in background thread
                    {
                        try
                        {
                            using (var tagFile = TagLib.File.Create(filePath))
                            {
                                properties.Width = tagFile.Properties.VideoWidth; // Use setter
                                properties.Height = tagFile.Properties.VideoHeight; // Use setter
                                properties.Duration = tagFile.Properties.Duration; // TagLib Duration is TimeSpan
                                properties.ImageFormat = tagFile.Properties.Description; // Use Description for format/codec info
                            }
                        }
                        catch (CorruptFileException ex)
                        {
                            Debug.WriteLine($"TagLib CorruptFileException ({filePath}): {ex.Message}");
                            // Try to load basic info even if tags are corrupt
                            try
                            {
                                using (var tagFile = TagLib.File.Create(filePath, ReadStyle.None))
                                {
                                    properties.Width = tagFile.Properties.VideoWidth;
                                    properties.Height = tagFile.Properties.VideoHeight;
                                    properties.Duration = tagFile.Properties.Duration;
                                }
                            }
                            catch (Exception innerEx)
                            {
                                Debug.WriteLine($"TagLib fallback ReadStyle.None failed ({filePath}): {innerEx.Message}");
                            }
                        }
                        catch (UnsupportedFormatException ex)
                        {
                            Debug.WriteLine($"TagLib UnsupportedFormatException ({filePath}): {ex.Message}");
                        }
                        catch (Exception ex) // Catch other potential TagLib errors
                        {
                            Debug.WriteLine($"TagLib 読み取りエラー ({filePath}): {ex.Message}");
                        }
                    });
                }
                else if (FileHelper.IsImageFile(filePath)) // 画像ファイルの場合
                {
                    properties.IsVideo = false; // Use setter
                    await Task.Run(() => // Image processing in background thread
                    {
                        try
                        {
                            // SkiaSharp を使用して画像の基本情報を読み取る
                            using (var stream = System.IO.File.OpenRead(filePath)) // Use fully qualified name
                            using (var skStream = new SKManagedStream(stream))
                            using (var codec = SKCodec.Create(skStream))
                            {
                                if (codec != null)
                                {
                                    var info = codec.Info;
                                    properties.Width = info.Width; // Use setter
                                    properties.Height = info.Height; // Use setter
                                    properties.ImageFormat = codec.EncodedFormat.ToString(); // Use setter
                                    properties.ColorDepth = GetColorDepth(codec); // Use setter
                                }
                            }

                            // Exif情報の読み取り (画像ファイルのみ)
                            ReadExifData(filePath, properties);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"画像プロパティ読み取りエラー ({filePath}): {ex.Message}");
                        }
                    });
                }
                // else: Handle unsupported file types if necessary
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"プロパティ読み込みエラー ({filePath}): {ex.Message}");
            }

            return properties;
        }

        private static void ReadExifData(string filePath, ImagePropertiesModel properties)
        {
            try
            {
                // Stable Diffusionメタデータを解析
                var metadata = StableDiffusionMetadataManager.ExtractMetadataFromFileAsync(filePath).GetAwaiter().GetResult();
                if (metadata.HasMetadata)
                {
                    // メタデータをプロパティに設定
                    properties.UserComment = metadata.RawMetadata;
                    // 解析成功した場合のみStableDiffusionResultを設定
                    if (metadata.ParseSuccess)
                    {
                        properties.SetStableDiffusionData(metadata);
                    }
                }

                // Exif情報を読み取る（Stable Diffusionメタデータがあっても一般的なEXIFは読み取る）
                var directories = ImageMetadataReader.ReadMetadata(filePath);
                var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();

                if (exif != null)
                {
                    // UserCommentがまだ設定されていない場合のみ設定
                    if (string.IsNullOrEmpty(properties.UserComment))
                    {
                        try
                        {
                            properties.UserComment = exif.GetDescription(ExifDirectoryBase.TagUserComment) ?? string.Empty;
                        }
                        catch
                        {
                            // 無視
                        }
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

                    // ダブルクォートから始まるカメラ情報は無効値として扱う
                    bool isValidMake = !string.IsNullOrEmpty(make) && !make.StartsWith("workflow", StringComparison.OrdinalIgnoreCase);
                    bool isValidModel = !string.IsNullOrEmpty(model) && !model.StartsWith("prompt", StringComparison.OrdinalIgnoreCase);

                    if (isValidModel)
                    {
                        if (isValidMake)
                        {
                            properties.CameraModel = $"{make} {model}";
                        }
                        else
                        {
                            properties.CameraModel = model;
                        }
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

        // --- Video/Duration Related Properties ---
        public bool IsVideo
        {
            get => _isVideo;
            private set // Set internally by LoadFromFileAsync
            {
                if (_isVideo != value)
                {
                    _isVideo = value;
                    OnPropertyChanged(nameof(IsVideo));
                    OnPropertyChanged(nameof(Dimensions)); // Dimensions depends on IsVideo
                }
            }
        }

        public TimeSpan Duration
        {
            get => _duration;
            private set // Set internally by LoadFromFileAsync
            {
                if (_duration != value)
                {
                    _duration = value;
                    OnPropertyChanged(nameof(Duration));
                    OnPropertyChanged(nameof(DurationFormatted)); // Formatted string depends on this
                    if (IsVideo) OnPropertyChanged(nameof(Dimensions)); // Dimensions depends on Duration for video
                }
            }
        }
    }

}
