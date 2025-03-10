using System.ComponentModel;
using System.Windows.Media.Imaging;
using System.Runtime.CompilerServices;
using System.IO;
using Illustra.Helpers;
using LinqToDB.Mapping;

namespace Illustra.Models
{
    public enum ThumbnailState
    {
        NotLoaded,    // まだロードされていない
        Loading,      // ロード中
        Loaded,       // 正常にロードされた
        Error         // ロード中にエラー発生
    }
    [Table(Name = "FileNodeModel")]
    public class FileNodeModel : INotifyPropertyChanged
    {
        public FileNodeModel()
        {
            // デフォルトコンストラクタ（データベースからの復元用）
        }

        public FileNodeModel(string filePath, ThumbnailInfo? thumbnailInfo = null)
        {
            FullPath = filePath;
            FolderPath = Path.GetDirectoryName(filePath) ?? string.Empty;
            FileType = Path.GetExtension(filePath);
            FileName = Path.GetFileName(filePath);
            IsImage = IsImageExtension(FileType);
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                CreationTime = fileInfo.CreationTime;
                LastModified = fileInfo.LastWriteTime;
                FileSize = fileInfo.Length;
            }
            ThumbnailInfo = thumbnailInfo ?? new ThumbnailInfo(null, ThumbnailState.NotLoaded);
        }

        private bool IsImageExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return false;
            extension = extension.ToLowerInvariant();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png" ||
                   extension == ".gif" || extension == ".bmp" || extension == ".webp";
        }

        private bool _isSelected = false;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        private string _fileName = string.Empty;
        [Column, NotNull]
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

        [Column, PrimaryKey, NotNull]
        public string FullPath
        {
            get => _fullPath;
            set
            {
                if (_fullPath != value)
                {
                    _fullPath = value;
                    OnPropertyChanged(nameof(FullPath));
                }
            }
        }
        private string _fullPath = string.Empty;

        // フォルダでフィルタするためのカラム
        [Column, NotNull]
        public string FolderPath
        {
            get => _folderPath;
            set
            {
                if (_folderPath != value)
                {
                    _folderPath = value;
                    OnPropertyChanged(nameof(FolderPath));
                }
            }
        }
        private string _folderPath = string.Empty;

        private ThumbnailInfo? _thumbnailInfo = null;
        public ThumbnailInfo? ThumbnailInfo
        {
            get => _thumbnailInfo;
            set
            {
                if (_thumbnailInfo != value)
                {
                    _thumbnailInfo = value;
                    OnPropertyChanged(nameof(ThumbnailInfo));
                }
            }
        }

        [Column, NotNull]
        public DateTime CreationTime { get; set; }

        [Column, NotNull]
        public DateTime LastModified { get; set; }

        [Column, NotNull]
        public long FileSize { get; set; }

        [Column, NotNull]
        public string FileType { get; set; } = string.Empty;

        private int _rating;

        [Column, NotNull]
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

        private bool _isImage;
        [Column, NotNull]
        public bool IsImage
        {
            get => _isImage;
            set
            {
                if (_isImage != value)
                {
                    _isImage = value;
                    OnPropertyChanged(nameof(IsImage));
                }
            }
        }

        private bool _isLastSelected;
        public bool IsLastSelected
        {
            get => _isLastSelected;
            set
            {
                if (_isLastSelected != value)
                {
                    _isLastSelected = value;
                    OnPropertyChanged(nameof(IsLastSelected));
                }
            }
        }

        // List View に変更を通知する。データベースへの連携には使わないこと
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ThumbnailInfo
    {
        public BitmapSource? Thumbnail { get; set; }
        public ThumbnailState State { get; set; }

        public ThumbnailInfo(BitmapSource? thumbnail, ThumbnailState state)
        {
            Thumbnail = thumbnail;
            State = state;
        }
    }
}
