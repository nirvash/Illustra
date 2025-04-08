using Illustra.Helpers;
using System;
using System.IO;
using System.Threading.Tasks;
using ImageMagick;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;

namespace Illustra.Services
{
    public class WebpAnimationService : IWebpAnimationService
    {
        private MagickImageCollection _collection;

        public async Task<MagickImageCollection> LoadAsync(string filePath)
        {
            LogHelper.LogWithTimestamp("Service.LoadAsync - Start", LogHelper.Categories.Performance);
            if (!File.Exists(filePath))
                throw new FileNotFoundException("WebPファイルが見つかりません", filePath);

            LogHelper.LogWithTimestamp("Service.LoadAsync - Before Task.Run", LogHelper.Categories.Performance);
            return await Task.Run(() =>
            {
                _collection?.Dispose(); // 以前のコレクションがあれば解放
                LogHelper.LogWithTimestamp("Service.LoadAsync - Task.Run Start", LogHelper.Categories.Performance);
                _collection = new MagickImageCollection(filePath);
                _collection.Coalesce(); // フレームを合成して完全な状態にする
                LogHelper.LogWithTimestamp("Service.LoadAsync - Before new MagickImageCollection", LogHelper.Categories.Performance);
                return _collection;
            });
        }

        public BitmapSource GetFrameAsBitmapSource(MagickImageCollection collection, int index)
        {
            if (collection == null || index < 0 || index >= collection.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            using (var frame = (MagickImage)collection[index].Clone())
            {
                frame.Format = MagickFormat.Bmp;
                using (var ms = new MemoryStream())
                {
                    frame.Write(ms);
                    ms.Position = 0;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
        }

        public int GetTotalFrames(MagickImageCollection collection)
        {
            return collection?.Count ?? 0;
        }

        public TimeSpan GetFrameDelay(MagickImageCollection collection, int index)
        {
            if (collection == null || index < 0 || index >= collection.Count)
                return TimeSpan.Zero;

            var delay = collection[index].AnimationDelay; // 1/100秒単位
            return TimeSpan.FromMilliseconds(delay * 10);
        }

        public TimeSpan GetTotalDuration(MagickImageCollection collection)
        {
            if (collection == null) return TimeSpan.Zero;

            double totalMs = 0;
            foreach (var frame in collection)
            {
                totalMs += frame.AnimationDelay * 10;
            }
            return TimeSpan.FromMilliseconds(totalMs);
        }

        public int GetLoopCount(MagickImageCollection collection)
        {
            if (collection == null || collection.Count == 0) return 0;
            return (int)collection[0].AnimationIterations;
        }

        public void Dispose()
        {
            _collection?.Dispose();
            _collection = null;
        }
    }
}
