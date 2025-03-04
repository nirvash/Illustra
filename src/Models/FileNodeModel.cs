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
        // 一時的にデータベース更新を停止するフラグ
        private static bool _suppressRatingUpdates = false;

        public FileNodeModel()
        {
            // デフォルトコンストラクタ（データベースからの復元用）
        }

        public FileNodeModel(string filePath, ThumbnailInfo? thumbnailInfo = null)
        {
            FullPath = filePath;
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                CreationTime = fileInfo.CreationTime;
                LastModified = fileInfo.LastWriteTime;
                FileSize = fileInfo.Length;
                FileType = Path.GetExtension(filePath);
                FileName = Path.GetFileName(filePath);
                IsImage = IsImageExtension(FileType);
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
                    FolderPath = Path.GetDirectoryName(value) ?? string.Empty;
                    OnPropertyChanged(nameof(FullPath));
                    OnPropertyChanged(nameof(FolderPath));
                }
            }
        }
        private string _fullPath = string.Empty;

        // フォルダでフィルタするためのカラム
        [Column, NotNull]
        public string FolderPath { get; set; } = string.Empty;

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
                    _rating = Math.Max(0, Math.Min(5, value)); // 0-5の範囲に制限
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

        private static readonly DatabaseManager _db = new();

        /// <summary>
        /// 一時的にレーティング更新を抑制します
        /// </summary>
        /// <param name="suppress">trueで抑制、falseで再開</param>
        public static void SuppressRatingUpdates(bool suppress)
        {
            _suppressRatingUpdates = suppress;
        }

        /// <summary>
        /// ファイルノードをデータベースに保存します
        /// </summary>
        public async Task SaveAsync()
        {
            await _db.SaveFileNodeAsync(this);
        }

        /// <summary>
        /// レーティングを更新してデータベースに保存します
        /// </summary>
        private async Task SaveRatingAsync(int rating)
        {
            try
            {
                await _db.UpdateRatingAsync(FullPath, rating);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"レーティングの保存中にエラーが発生: {ex.Message}");
            }
        }

        /// <summary>
        /// レーティングを強制的に保存します（バッチ操作後の更新用）
        /// </summary>
        public async Task ForceSaveRatingAsync()
        {
            await SaveRatingAsync(_rating);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
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
