using System.ComponentModel;
using System.Windows.Media.Imaging;
using System.Runtime.CompilerServices;
using System.IO;
using Illustra.Helpers;
using LinqToDB.Mapping;
using System.Diagnostics;

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

            // ThumbnailInfoのPropertyChangedイベントを購読
            if (_thumbnailInfo != null)
            {
                _thumbnailInfo.PropertyChanged += OnThumbnailInfoPropertyChanged;
            }
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

        private ThumbnailInfo _thumbnailInfo;
        [LinqToDB.Mapping.NotColumn]
        public ThumbnailInfo ThumbnailInfo
        {
            get => _thumbnailInfo;
            set
            {
                if (_thumbnailInfo != value)
                {
                    // 古いThumbnailInfoのイベント購読を解除
                    if (_thumbnailInfo != null)
                    {
                        _thumbnailInfo.PropertyChanged -= OnThumbnailInfoPropertyChanged;
                    }

                    _thumbnailInfo = value;

                    // 新しいThumbnailInfoのイベント購読を設定
                    if (_thumbnailInfo != null)
                    {
                        _thumbnailInfo.PropertyChanged += OnThumbnailInfoPropertyChanged;
                    }

                    OnPropertyChanged(nameof(ThumbnailInfo));
                    // HasThumbnailプロパティも更新
                    HasThumbnail = value?.State == ThumbnailState.Loaded;
                    IsLoadingThumbnail = value?.State == ThumbnailState.Loading;
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
        [LinqToDB.Mapping.NotColumn]
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

        ~FileNodeModel()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_thumbnailInfo != null)
                {
                    _thumbnailInfo.PropertyChanged -= OnThumbnailInfoPropertyChanged;
                    if (_thumbnailInfo.Image != null)
                    {
                        _thumbnailInfo.Image = null;
                    }
                    _thumbnailInfo = null;
                }
            }
        }

        private bool _hasThumbnail;
        // データベースに永続化しないプロパティ
        [LinqToDB.Mapping.NotColumn]
        public bool HasThumbnail
        {
            get => _hasThumbnail;
            set
            {
                if (_hasThumbnail != value)
                {
                    _hasThumbnail = value;
                    OnPropertyChanged(nameof(HasThumbnail));
                }
            }
        }

        private bool _isLoadingThumbnail;
        // データベースに永続化しないプロパティ
        [LinqToDB.Mapping.NotColumn]
        public bool IsLoadingThumbnail
        {
            get => _isLoadingThumbnail;
            set
            {
                if (_isLoadingThumbnail != value)
                {
                    _isLoadingThumbnail = value;
                    OnPropertyChanged(nameof(IsLoadingThumbnail));
                }
            }
        }

        // サムネイルを設定するメソッド
        public void SetThumbnail(BitmapSource thumbnail)
        {
            ThumbnailInfo = new ThumbnailInfo(thumbnail, ThumbnailState.Loaded);
            HasThumbnail = thumbnail != null;
        }

        // OnThumbnailInfoPropertyChangedメソッドを追加
        private void OnThumbnailInfoPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // ThumbnailInfoのプロパティが変更されたときの処理
            if (e.PropertyName == nameof(ThumbnailInfo.State))
            {
                // 状態が変更されたら、HasThumbnailとIsLoadingThumbnailを更新
                HasThumbnail = _thumbnailInfo?.State == ThumbnailState.Loaded;
                IsLoadingThumbnail = _thumbnailInfo?.State == ThumbnailState.Loading;
                OnPropertyChanged(nameof(ThumbnailInfo));
            }
            else if (e.PropertyName == nameof(ThumbnailInfo.Image))
            {
                // 画像が変更されたら明示的に通知
                OnPropertyChanged(nameof(ThumbnailInfo));
            }
        }
    }

    public class ThumbnailInfo : INotifyPropertyChanged
    {
        private BitmapSource _image;
        private ThumbnailState _state;

        public BitmapSource Image
        {
            get => _image;
            set
            {
                if (_image != value)
                {
                    _image = value;
                    OnPropertyChanged(nameof(Image));
                }
            }
        }

        public ThumbnailState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged(nameof(State));
                    OnPropertyChanged(nameof(IsLoadingThumbnail));
                    OnPropertyChanged(nameof(HasThumbnail));
                }
            }
        }

        // IsLoadingThumbnailをプロパティとして実装
        public bool IsLoadingThumbnail
        {
            get => State == ThumbnailState.Loading;
            set
            {
                if (value)
                {
                    State = ThumbnailState.Loading;
                }
                else if (State == ThumbnailState.Loading)
                {
                    State = ThumbnailState.NotLoaded;
                }
            }
        }

        // HasThumbnailをプロパティとして実装
        public bool HasThumbnail
        {
            get => State == ThumbnailState.Loaded;
        }

        public ThumbnailInfo(BitmapSource image, ThumbnailState state)
        {
            _image = image;
            _state = state;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
