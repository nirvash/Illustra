using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using Illustra.Models;
using Illustra.Helpers.Interfaces;

namespace Illustra.Helpers
{
    /// <summary>
    /// ウィンドウベースの画像キャッシュ実装
    /// </summary>
    public class WindowBasedImageCache : IImageCache
    {
        private readonly int _forwardSize;
        private readonly int _backwardSize;
        private readonly Dictionary<string, BitmapSource> _cache;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="forwardSize">現在位置より後ろにキャッシュする数</param>
        /// <param name="backwardSize">現在位置より前にキャッシュする数</param>
        public WindowBasedImageCache(int forwardSize = 3, int backwardSize = 3)
        {
            _forwardSize = forwardSize;
            _backwardSize = backwardSize;
            _cache = new Dictionary<string, BitmapSource>();
        }

        /// <inheritdoc/>
        public BitmapSource GetImage(string path)
        {
            if (_cache.TryGetValue(path, out var image))
            {
                return image;
            }

            // キャッシュミスした場合は読み込んでキャッシュに追加
            try
            {
                var newImage = LoadImageFromFile(path);
                _cache[path] = newImage;
                return newImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"画像の読み込みエラー: {ex.Message}");
                throw; // 呼び出し元に例外を伝播
            }
        }

        /// <inheritdoc/>
        public bool HasImage(string path)
        {
            return _cache.ContainsKey(path);
        }

        /// <inheritdoc/>
        public void UpdateCache(List<FileNodeModel> files, int currentIndex)
        {
            // 動画ファイルを除外したリストを作成
            var imageFiles = files.Where(f => FileHelper.IsImageFile(f.FullPath)).ToList();

            // 現在のファイルが画像リスト内に存在するか確認し、インデックスを取得
            // 現在のファイルが動画の場合、キャッシュ更新は行わない（または最近傍の画像インデックスを使用するなどの代替策）
            var currentImageFile = imageFiles.FirstOrDefault(f => f.FullPath == files[currentIndex].FullPath);
            if (currentImageFile == null)
            {
                 // 現在表示中のファイルが画像でない場合、キャッシュ更新ロジックをスキップ
                 // 必要に応じて、既存キャッシュのクリーンアップのみ実行
                 CleanUpCache(new HashSet<string>(imageFiles.Select(f => f.FullPath))); // 画像ファイルのみ保持
                 return;
            }
            int imageCurrentIndex = imageFiles.IndexOf(currentImageFile);


            // キャッシュウィンドウの範囲を計算 (画像リスト基準)
            int startIndex = Math.Max(0, imageCurrentIndex - _backwardSize);
            int endIndex = Math.Min(imageFiles.Count - 1, imageCurrentIndex + _forwardSize);


            // 新しいウィンドウに含まれるべきパスを収集 (画像ファイルのみ)
            var pathsToKeep = new HashSet<string>();
             if (startIndex <= endIndex && startIndex >= 0 && endIndex < imageFiles.Count) // 範囲チェック
             {
                for (int i = startIndex; i <= endIndex; i++)
                {
                    pathsToKeep.Add(imageFiles[i].FullPath);
                }
             }

            // 不要なキャッシュエントリを削除するヘルパーメソッドを呼び出す
            CleanUpCache(pathsToKeep);

            // 新しいアイテムをキャッシュに追加 (画像ファイルのみ)
            if (startIndex <= endIndex && startIndex >= 0 && endIndex < imageFiles.Count) // 範囲チェック
            {
                for (int i = startIndex; i <= endIndex; i++)
                {
                    var imagePath = imageFiles[i].FullPath;
                    if (!_cache.ContainsKey(imagePath))
                    {
                        // キャッシュサイズ制限チェック（追加前）
                        if (_cache.Count >= (_forwardSize + _backwardSize + 1)) // キャッシュ最大サイズチェック
                        {
                            // 最も古いエントリを削除するロジックが必要 (ここでは省略)
                            // RemoveOldestCacheEntry(); // 仮のメソッド呼び出し
                        }

                        try
                        {
                            var image = LoadImageFromFile(imagePath);
                            _cache[imagePath] = image;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"画像のプリロードエラー: {ex.Message}");
                            // プリロード時のエラーは無視
                        }
                    }
                }
            }
        }

        // 不要なキャッシュエントリを削除するヘルパーメソッド
        private void CleanUpCache(HashSet<string> pathsToKeep)
        {
            // 範囲外になったアイテムをキャッシュから削除
            var pathsToRemove = new List<string>();
            foreach (var path in _cache.Keys)
            {
                if (!pathsToKeep.Contains(path))
                {
                    pathsToRemove.Add(path);
                }
            }

            foreach (var path in pathsToRemove)
            {
                _cache.Remove(path);
            }
        }


        /// <inheritdoc/>
        public void Clear()
        {
            _cache.Clear();
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, BitmapSource> CachedItems => _cache;

        /// <summary>
        /// ファイルから画像を読み込む
        /// </summary>
        private static BitmapSource LoadImageFromFile(string path)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
