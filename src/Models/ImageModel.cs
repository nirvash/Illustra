using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Illustra.Helpers;
using Illustra.Models;

namespace Illustra.Models
{
    /// <summary>
    /// 画像ファイルの操作と管理を担当するモデルクラス
    /// </summary>
    public class ImageModel : INotifyPropertyChanged
    {
        private readonly DatabaseManager _db = new();

        /// <summary>
        /// 画像ファイルのコレクション
        /// </summary>
        public BulkObservableCollection<FileNodeModel> Items { get; } = new();

        /// <summary>
        /// 現在のフォルダパス
        /// </summary>
        private string? _currentFolderPath;
        public string? CurrentFolderPath
        {
            get => _currentFolderPath;
            private set
            {
                if (_currentFolderPath != value)
                {
                    _currentFolderPath = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public ImageModel()
        {
        }

        /// <summary>
        /// 指定されたフォルダ内の画像ファイルを読み込む
        /// </summary>
        /// <param name="folderPath">読み込むフォルダのパス</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>読み込まれたファイル数</returns>
        public async Task<int> LoadImagesFromFolderAsync(string folderPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                return 0;
            }

            CurrentFolderPath = folderPath;
            Items.Clear();

            try
            {
                var imageFiles = Directory.GetFiles(folderPath)
                    .Where(file => IsImageFile(file))
                    .ToList();

                foreach (var file in imageFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var fileNode = new FileNodeModel(file);
                    Items.Add(fileNode);

                    // データベースから追加情報を読み込む
                    await LoadAdditionalInfoAsync(fileNode);
                }

                return Items.Count;
            }
            catch (Exception ex)
            {
                // エラーログ記録などの処理
                System.Diagnostics.Debug.WriteLine($"Error loading images: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// ファイルが画像ファイルかどうかを判定
        /// </summary>
        private bool IsImageFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png" ||
                   extension == ".gif" || extension == ".bmp" || extension == ".webp";
        }

        /// <summary>
        /// データベースから追加情報を読み込む
        /// </summary>
        private async Task LoadAdditionalInfoAsync(FileNodeModel fileNode)
        {
            try
            {
                var dbItem = await _db.GetFileNodeAsync(fileNode.FullPath);
                if (dbItem != null)
                {
                    fileNode.Rating = dbItem.Rating;
                    // その他の必要な情報を設定
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading additional info: {ex.Message}");
            }
        }

        /// <summary>
        /// 基本的なフィルタリング処理
        /// </summary>
        /// <param name="rating">レーティングフィルタ（0=フィルタなし）</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>フィルタリングされたアイテムのリスト</returns>
        public async Task<List<FileNodeModel>> FilterByRatingAsync(int rating, CancellationToken cancellationToken = default)
        {
            if (rating <= 0)
            {
                return Items.ToList(); // フィルタなし
            }

            return await Task.Run(() =>
            {
                var result = new List<FileNodeModel>();
                foreach (var item in Items)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (item.Rating >= rating)
                    {
                        result.Add(item);
                    }
                }
                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// 基本的なソート処理
        /// </summary>
        /// <param name="sortByDate">日付でソートするかどうか</param>
        /// <param name="sortAscending">昇順でソートするかどうか</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>ソートされたアイテムのリスト</returns>
        public async Task<List<FileNodeModel>> SortItemsAsync(bool sortByDate, bool sortAscending, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                IEnumerable<FileNodeModel> query = Items;

                if (sortByDate)
                {
                    query = sortAscending
                        ? query.OrderBy(item => item.LastModified)
                        : query.OrderByDescending(item => item.LastModified);
                }
                else
                {
                    query = sortAscending
                        ? query.OrderBy(item => item.FileName)
                        : query.OrderByDescending(item => item.FileName);
                }

                return query.ToList();
            }, cancellationToken);
        }

        /// <summary>
        /// プロパティ変更通知イベント
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// プロパティ変更通知
        /// </summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}