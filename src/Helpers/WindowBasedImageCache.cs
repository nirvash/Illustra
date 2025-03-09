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
            // キャッシュウィンドウの範囲を計算
            int startIndex = Math.Max(0, currentIndex - _backwardSize);
            int endIndex = Math.Min(files.Count - 1, currentIndex + _forwardSize);

            // 新しいウィンドウに含まれるべきパスを収集
            var pathsToKeep = new HashSet<string>();
            for (int i = startIndex; i <= endIndex; i++)
            {
                pathsToKeep.Add(files[i].FullPath);
            }

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

            // 新しいアイテムをキャッシュに追加
            foreach (var path in pathsToKeep)
            {
                if (!_cache.ContainsKey(path))
                {
                    try
                    {
                        var image = LoadImageFromFile(path);
                        _cache[path] = image;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"画像のプリロードエラー: {ex.Message}");
                        // プリロード時のエラーは無視（オンデマンドで再試行される）
                    }
                }
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
