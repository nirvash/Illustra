using System.ComponentModel;
using System.Windows.Media.Imaging;
using System.Runtime.CompilerServices;
using System.IO;
using Illustra.Helpers;

namespace Illustra.Models
{
    public enum ThumbnailState
    {
        NotLoaded,    // まだロードされていない
        Loading,      // ロード中
        Loaded,       // 正常にロードされた
        Error         // ロード中にエラー発生
    }

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
                Name = Path.GetFileName(filePath);
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

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                    OnPropertyChanged(nameof(FileName)); // 連動させる
                }
            }
        }

        public string FullPath { get; set; } = string.Empty;

        public string FolderPath
        {
            get => Path.GetDirectoryName(FullPath) ?? string.Empty;
        }

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

        public DateTime CreationTime { get; set; }

        public DateTime LastModified { get; set; }

        public long FileSize { get; set; }

        public string FileType { get; set; } = string.Empty;

        // データベースと連携するためのプロパティ
        // Name -> FileName へのマッピング
        public string FileName
        {
            get => Name;
            set => Name = value;
        }

        private int _rating;
        public int Rating
        {
            get => _rating;
            set
            {
                if (_rating != value)
                {
                    _rating = Math.Max(0, Math.Min(5, value)); // 0-5の範囲に制限
                    OnPropertyChanged(nameof(Rating));

                    // レーティング更新が抑制されていない場合のみデータベースを更新
                    if (!_suppressRatingUpdates)
                    {
                        _ = SaveRatingAsync(_rating);  // データベース更新を非同期で実行
                    }
                }
            }
        }

        private bool _isImage;
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
