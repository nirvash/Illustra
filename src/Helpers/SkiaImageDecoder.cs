using System;
using System.IO;
using SkiaSharp;

namespace Illustra.Helpers
{
    /// <summary>初回フレームだけを、透明度でRGBを乗算しないBGRAとして読む。</summary>
    public static class SkiaImageDecoder
    {
        private const long MaxEncodedBytes = 128L * 1024 * 1024;
        private const long MaxImagePixels = 32L * 1024 * 1024;

        public static SKBitmap DecodeFirstFrame(string path)
        {
            using var file = File.OpenRead(path);
            if (file.Length == 0 || file.Length > MaxEncodedBytes)
                throw new InvalidDataException("画像ファイルのサイズが上限を超えています。");
            using var stream = new SKManagedStream(file);
            using var codec = SKCodec.Create(stream);
            if (codec == null) throw new InvalidDataException("画像を読み取れません。");
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxImagePixels)
                throw new InvalidDataException("画像のピクセル数が上限を超えています。");

            // WPF Bgra32とalpha LSBのため、既定のPremulデコードを使わない。
            var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height,
                SKColorType.Bgra8888, SKAlphaType.Unpremul));
            try
            {
                if (bitmap.GetPixels() == IntPtr.Zero ||
                    codec.GetPixels(bitmap.Info, bitmap.GetPixels(), new SKCodecOptions(0)) != SKCodecResult.Success)
                    throw new InvalidDataException("画像の初回フレームを読み取れません。");
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }
    }
}
