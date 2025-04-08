using System;
using System.Threading.Tasks;
using ImageMagick;
using System.Windows.Media.Imaging;

namespace Illustra.Services
{
    public interface IWebpAnimationService : IDisposable
    {
        /// <summary>
        /// WebPファイルを非同期で読み込み、MagickImageCollectionを返す
        /// </summary>
        /// <param name="filePath">WebPファイルのパス</param>
        /// <returns>MagickImageCollection</returns>
        Task<MagickImageCollection> LoadAsync(string filePath);

        /// <summary>
        /// 指定インデックスのフレームをBitmapSourceに変換して返す
        /// </summary>
        BitmapSource GetFrameAsBitmapSource(MagickImageCollection collection, int index);

        /// <summary>
        /// 総フレーム数を取得
        /// </summary>
        int GetTotalFrames(MagickImageCollection collection);

        /// <summary>
        /// 指定フレームの遅延時間を取得
        /// </summary>
        TimeSpan GetFrameDelay(MagickImageCollection collection, int index);

        /// <summary>
        /// 全フレームの遅延時間合計を取得
        /// </summary>
        TimeSpan GetTotalDuration(MagickImageCollection collection);

        /// <summary>
        /// ループ回数を取得 (0は無限ループ)
        /// </summary>
        int GetLoopCount(MagickImageCollection collection);
    }
}
