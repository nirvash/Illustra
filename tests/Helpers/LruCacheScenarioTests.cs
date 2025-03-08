using System;
using System.Collections.Generic;
using System.Linq;
using Illustra.Helpers;
using NUnit.Framework;

namespace Illustra.Tests.Helpers
{
    [TestFixture]
    public class LruCacheScenarioTests
    {
        // 画像ビューワーのシナリオをシミュレートするテスト
        [Test]
        public void ImageViewerScenario_ForwardNavigation()
        {
            // Arrange
            var cache = new LruCache<string, string>(5);
            var imagePaths = new List<string>
            {
                "/images/img1.jpg",
                "/images/img2.jpg",
                "/images/img3.jpg",
                "/images/img4.jpg",
                "/images/img5.jpg",
                "/images/img6.jpg",
                "/images/img7.jpg",
                "/images/img8.jpg",
                "/images/img9.jpg",
                "/images/img10.jpg"
            };

            // Act & Assert

            // 最初の画像を表示
            string currentImage = imagePaths[0];
            cache.Add(currentImage, "image-data-1");

            // 前後の画像をプリロード
            PreloadImages(cache, imagePaths, currentImage, 0, 1);

            // 次の画像に移動
            string previousImage = currentImage;
            currentImage = imagePaths[1];

            // キャッシュから読み込み（または新規追加）
            if (!cache.TryGetValue(currentImage, out var imageData))
            {
                cache.Add(currentImage, "image-data-2");
            }
            else
            {
                // キャッシュヒットした場合でも、LRUの順序を更新
                cache.Add(currentImage, imageData);
            }

            // 前後の画像をプリロード（移動方向を考慮）
            PreloadImages(cache, imagePaths, currentImage, 1, 1);

            // さらに次の画像に移動
            previousImage = currentImage;
            currentImage = imagePaths[2];

            if (!cache.TryGetValue(currentImage, out imageData))
            {
                cache.Add(currentImage, "image-data-3");
            }
            else
            {
                cache.Add(currentImage, imageData);
            }

            // 前後の画像をプリロード（移動方向を考慮）
            PreloadImages(cache, imagePaths, currentImage, 2, 1);

            // キャッシュの状態を確認
            var keys = cache.GetKeys().ToList();

            // 最も最近使用された画像は現在の画像
            Assert.AreEqual(currentImage, keys[0]);

            // キャッシュには5つの画像が含まれている
            Assert.AreEqual(5, cache.Count);

            // 移動方向（前方）の画像がキャッシュに含まれている
            Assert.IsTrue(cache.TryGetValue(imagePaths[3], out _));
            Assert.IsTrue(cache.TryGetValue(imagePaths[4], out _));
        }

        [Test]
        public void ImageViewerScenario_BackwardNavigation()
        {
            // Arrange
            var cache = new LruCache<string, string>(5);
            var imagePaths = new List<string>
            {
                "/images/img1.jpg",
                "/images/img2.jpg",
                "/images/img3.jpg",
                "/images/img4.jpg",
                "/images/img5.jpg",
                "/images/img6.jpg",
                "/images/img7.jpg",
                "/images/img8.jpg",
                "/images/img9.jpg",
                "/images/img10.jpg"
            };

            // Act & Assert

            // 中間の画像から開始
            string currentImage = imagePaths[5];
            cache.Add(currentImage, "image-data-6");

            // 前後の画像をプリロード
            PreloadImages(cache, imagePaths, currentImage, 5, 0);

            // 前の画像に移動
            string previousImage = currentImage;
            currentImage = imagePaths[4];

            // キャッシュから読み込み（または新規追加）
            if (!cache.TryGetValue(currentImage, out var imageData))
            {
                cache.Add(currentImage, "image-data-5");
            }
            else
            {
                // キャッシュヒットした場合でも、LRUの順序を更新
                cache.Add(currentImage, imageData);
            }

            // 前後の画像をプリロード（移動方向を考慮）
            PreloadImages(cache, imagePaths, currentImage, 4, -1);

            // さらに前の画像に移動
            previousImage = currentImage;
            currentImage = imagePaths[3];

            if (!cache.TryGetValue(currentImage, out imageData))
            {
                cache.Add(currentImage, "image-data-4");
            }
            else
            {
                cache.Add(currentImage, imageData);
            }

            // 前後の画像をプリロード（移動方向を考慮）
            PreloadImages(cache, imagePaths, currentImage, 3, -1);

            // キャッシュの状態を確認
            var keys = cache.GetKeys().ToList();

            // 最も最近使用された画像は現在の画像
            Assert.AreEqual(currentImage, keys[0]);

            // キャッシュには5つの画像が含まれている
            Assert.AreEqual(5, cache.Count);

            // 移動方向（後方）の画像がキャッシュに含まれている
            Assert.IsTrue(cache.TryGetValue(imagePaths[2], out _));
            Assert.IsTrue(cache.TryGetValue(imagePaths[1], out _));
        }

        [Test]
        public void ImageViewerScenario_DirectionChange()
        {
            // Arrange
            var cache = new LruCache<string, string>(5);
            var imagePaths = new List<string>
            {
                "/images/img1.jpg",
                "/images/img2.jpg",
                "/images/img3.jpg",
                "/images/img4.jpg",
                "/images/img5.jpg",
                "/images/img6.jpg",
                "/images/img7.jpg",
                "/images/img8.jpg",
                "/images/img9.jpg",
                "/images/img10.jpg"
            };

            // Act & Assert

            // 中間の画像から開始
            string currentImage = imagePaths[5];
            cache.Add(currentImage, "image-data-6");

            // 前後の画像をプリロード
            PreloadImages(cache, imagePaths, currentImage, 5, 0);

            // 前の画像に移動
            string previousImage = currentImage;
            currentImage = imagePaths[4];

            // キャッシュから読み込み（または新規追加）
            if (!cache.TryGetValue(currentImage, out var imageData))
            {
                cache.Add(currentImage, "image-data-5");
            }
            else
            {
                cache.Add(currentImage, imageData);
            }

            // 前後の画像をプリロード（移動方向を考慮）
            PreloadImages(cache, imagePaths, currentImage, 4, -1);

            // 方向を変えて次の画像に移動
            previousImage = currentImage;
            currentImage = imagePaths[5]; // 元の位置に戻る

            if (!cache.TryGetValue(currentImage, out imageData))
            {
                cache.Add(currentImage, "image-data-6");
            }
            else
            {
                cache.Add(currentImage, imageData);
            }

            // 前後の画像をプリロード（移動方向を考慮）
            PreloadImages(cache, imagePaths, currentImage, 5, 1);

            // キャッシュの状態を確認
            var keys = cache.GetKeys().ToList();

            // 最も最近使用された画像は現在の画像
            Assert.AreEqual(currentImage, keys[0]);

            // キャッシュには5つの画像が含まれている
            Assert.AreEqual(5, cache.Count);

            // 移動方向（前方）の画像がキャッシュに含まれている
            Assert.IsTrue(cache.TryGetValue(imagePaths[6], out _));
            Assert.IsTrue(cache.TryGetValue(imagePaths[7], out _));
        }

        [Test]
        public void ImageViewerScenario_WithOptimizedCaching()
        {
            // Arrange
            var cache = new LruCache<string, string>(5);
            var imagePaths = new List<string>
            {
                "/images/img1.jpg",
                "/images/img2.jpg",
                "/images/img3.jpg",
                "/images/img4.jpg",
                "/images/img5.jpg",
                "/images/img6.jpg",
                "/images/img7.jpg",
                "/images/img8.jpg",
                "/images/img9.jpg",
                "/images/img10.jpg"
            };

            // Act & Assert

            // 最初の画像を表示
            string currentImage = imagePaths[0];
            cache.Add(currentImage, "image-data-1");

            // 前後の画像をプリロード（存在確認にContainsKeyを使用）
            PreloadImagesOptimized(cache, imagePaths, currentImage, 0, 1);

            // 次の画像に移動
            string previousImage = currentImage;
            currentImage = imagePaths[1];

            // キャッシュから読み込み（または新規追加）
            if (!cache.TryGetValue(currentImage, out var imageData))
            {
                cache.Add(currentImage, "image-data-2");
            }
            else
            {
                // キャッシュヒットした場合でも、LRUの順序を更新
                cache.Add(currentImage, imageData);
            }

            // 前後の画像をプリロード（移動方向を考慮）
            PreloadImagesOptimized(cache, imagePaths, currentImage, 1, 1);

            // さらに次の画像に移動
            previousImage = currentImage;
            currentImage = imagePaths[2];

            if (!cache.TryGetValue(currentImage, out imageData))
            {
                cache.Add(currentImage, "image-data-3");
            }
            else
            {
                cache.Add(currentImage, imageData);
            }

            // 前後の画像をプリロード（移動方向を考慮）
            PreloadImagesOptimized(cache, imagePaths, currentImage, 2, 1);

            // キャッシュの状態を確認
            var keys = cache.GetKeys().ToList();

            // 最も最近使用された画像は現在の画像
            Assert.AreEqual(currentImage, keys[0]);

            // キャッシュには5つの画像が含まれている
            Assert.AreEqual(5, cache.Count);

            // 移動方向（前方）の画像がキャッシュに含まれている
            Assert.IsTrue(cache.ContainsKey(imagePaths[3]));
            Assert.IsTrue(cache.ContainsKey(imagePaths[4]));
        }

        // ヘルパーメソッド
        private void PreloadImages(LruCache<string, string> cache, List<string> imagePaths, string currentImage, int currentIndex, int direction)
        {
            // キャッシュする範囲を計算
            int cacheSize = cache.Capacity;
            int startIndex = Math.Max(0, currentIndex - cacheSize / 2);
            int endIndex = Math.Min(imagePaths.Count - 1, currentIndex + cacheSize / 2);

            // キャッシュする画像のパスを収集
            var pathsToCache = new List<string>();
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (i != currentIndex) // 現在の画像は既にキャッシュされているはず
                {
                    pathsToCache.Add(imagePaths[i]);
                }
            }

            // 移動方向に基づいて優先順位を設定
            if (direction != 0)
            {
                // 移動方向に応じてパスをソート
                pathsToCache.Sort((a, b) =>
                {
                    int indexA = imagePaths.IndexOf(a);
                    int indexB = imagePaths.IndexOf(b);

                    // 現在位置からの距離を計算
                    int distanceA = Math.Abs(indexA - currentIndex);
                    int distanceB = Math.Abs(indexB - currentIndex);

                    // 移動方向に合わせて優先順位を調整
                    if (direction > 0) // 次へ移動
                    {
                        // 現在位置より後ろの画像を優先
                        if (indexA > currentIndex && indexB <= currentIndex) return -1;
                        if (indexA <= currentIndex && indexB > currentIndex) return 1;
                    }
                    else // 前へ移動
                    {
                        // 現在位置より前の画像を優先
                        if (indexA < currentIndex && indexB >= currentIndex) return -1;
                        if (indexA >= currentIndex && indexB < currentIndex) return 1;
                    }

                    // 同じ方向なら距離が近い方を優先
                    return distanceA.CompareTo(distanceB);
                });
            }

            // 画像をキャッシュ
            foreach (var path in pathsToCache)
            {
                try
                {
                    if (!cache.TryGetValue(path, out var cachedImage))
                    {
                        // 実際のテストでは画像データの代わりに文字列を使用
                        cache.Add(path, $"image-data-{imagePaths.IndexOf(path) + 1}");
                    }
                }
                catch (Exception)
                {
                    // テストでは例外は無視
                }
            }
        }

        // 最適化されたヘルパーメソッド（ContainsKeyを使用）
        private void PreloadImagesOptimized(LruCache<string, string> cache, List<string> imagePaths, string currentImage, int currentIndex, int direction)
        {
            // キャッシュする範囲を計算
            int cacheSize = cache.Capacity;
            int startIndex = Math.Max(0, currentIndex - cacheSize / 2);
            int endIndex = Math.Min(imagePaths.Count - 1, currentIndex + cacheSize / 2);

            // キャッシュする画像のパスを収集
            var pathsToCache = new List<string>();
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (i != currentIndex) // 現在の画像は既にキャッシュされているはず
                {
                    pathsToCache.Add(imagePaths[i]);
                }
            }

            // 移動方向に基づいて優先順位を設定
            if (direction != 0)
            {
                // 移動方向に応じてパスをソート
                pathsToCache.Sort((a, b) =>
                {
                    int indexA = imagePaths.IndexOf(a);
                    int indexB = imagePaths.IndexOf(b);

                    // 現在位置からの距離を計算
                    int distanceA = Math.Abs(indexA - currentIndex);
                    int distanceB = Math.Abs(indexB - currentIndex);

                    // 移動方向に合わせて優先順位を調整
                    if (direction > 0) // 次へ移動
                    {
                        // 現在位置より後ろの画像を優先
                        if (indexA > currentIndex && indexB <= currentIndex) return -1;
                        if (indexA <= currentIndex && indexB > currentIndex) return 1;
                    }
                    else // 前へ移動
                    {
                        // 現在位置より前の画像を優先
                        if (indexA < currentIndex && indexB >= currentIndex) return -1;
                        if (indexA >= currentIndex && indexB < currentIndex) return 1;
                    }

                    // 同じ方向なら距離が近い方を優先
                    return distanceA.CompareTo(distanceB);
                });
            }

            // 画像をキャッシュ
            foreach (var path in pathsToCache)
            {
                try
                {
                    // ContainsKeyを使用して存在確認（LRU順序を更新しない）
                    if (!cache.ContainsKey(path))
                    {
                        // 実際のテストでは画像データの代わりに文字列を使用
                        cache.Add(path, $"image-data-{imagePaths.IndexOf(path) + 1}");
                    }
                }
                catch (Exception)
                {
                    // テストでは例外は無視
                }
            }
        }
    }
}
