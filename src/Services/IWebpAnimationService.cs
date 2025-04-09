using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Illustra.Helpers; // LibWebPのため

namespace Illustra.Services
{
    public interface IWebpAnimationService : IDisposable
    {
        /// <summary>
        /// 指定されたWebPファイルを読み込み、サービスを初期化します。
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        Task InitializeAsync(string filePath);

        /// <summary>
        /// 初期化されたWebPファイルの情報を非同期で取得します。
        /// </summary>
        /// <returns>フレーム情報 (幅, 高さ, アニメーション有無)</returns>
        LibWebP.WebPBitstreamFeatures GetFeatures(); // Now synchronous

        // StartDecodingAsync might be obsolete now, consider removing or redesigning
        // Task StartDecodingAsync(...)
        // DecodeAllFramesAsync and DecodeFirstFrameAsync are likely obsolete
        // Task<List<(BitmapSource Frame, TimeSpan Delay)>> DecodeAllFramesAsync(string filePath);
        // Task<BitmapSource> DecodeFirstFrameAsync(string filePath);

        /// <summary>
        /// WebPファイルの各フレームの遅延時間情報のみを取得します。
        /// </summary>
        /// <returns>各フレームの遅延時間のリスト (初期化後に取得)</returns>
        List<TimeSpan> GetFrameDelays(); // Now synchronous
        /// <summary>
        /// 指定されたインデックスのフレームをデコードします。
        /// </summary>
        /// <param name="index">0ベースのフレームインデックス</param>
        /// <returns>デコードされたフレーム</returns>
        Task<WebpAnimationService.WebPDecodedFrame> DecodeFrameAsync(int index);
    }
}
