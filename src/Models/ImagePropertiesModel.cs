using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

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

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
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
        }

        public static async Task<ImagePropertiesModel> LoadFromFileAsync(string filePath)
        {
            var model = new ImagePropertiesModel();
            
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Exists)
                {
                    model.FilePath = filePath;
                    model.FileName = fileInfo.Name;
                    model.FileSize = fileInfo.Length;
                    model.CreationTime = fileInfo.CreationTime;
                    model.LastModified = fileInfo.LastWriteTime;
                    model.FileType = fileInfo.Extension;
                    
                    // Load image dimensions if it's an image file
                    await Task.Run(() => {
                        try {
                            var imageInfo = new BitmapImage();
                            imageInfo.BeginInit();
                            imageInfo.UriSource = new Uri(filePath);
                            imageInfo.CacheOption = BitmapCacheOption.OnLoad;
                            imageInfo.EndInit();
                            
                            model.Dimensions = $"{imageInfo.PixelWidth} x {imageInfo.PixelHeight}";
                            
                            // Create a small preview
                            var preview = new TransformedBitmap(imageInfo, new System.Windows.Media.ScaleTransform(
                                100.0 / imageInfo.PixelWidth,
                                100.0 / imageInfo.PixelHeight));
                            model.Preview = preview;
                        }
                        catch {
                            // Not an image or couldn't load
                            model.Dimensions = "N/A";
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading file properties: {ex.Message}");
            }
            
            return model;
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
    }
}
