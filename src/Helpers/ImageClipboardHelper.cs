using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

namespace Illustra.Helpers
{
    public static class ImageClipboardHelper
    {
        public static BitmapSource LoadBitmapSource(string path)
        {
            using var image = SkiaImageDecoder.DecodeFirstFrame(path);
            var pixels = new byte[checked(image.RowBytes * image.Height)];
            Marshal.Copy(image.GetPixels(), pixels, 0, pixels.Length);
            var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96,
                PixelFormats.Bgra32, null, pixels, image.RowBytes);
            bitmap.Freeze();
            return bitmap;
        }

        public static void CopyImageToClipboard(string path)
        {
            if (!FileHelper.IsImageFile(path))
                throw new ArgumentException("指定されたファイルは画像ではありません。", nameof(path));

            Clipboard.SetImage(LoadBitmapSource(path));
        }
    }
}
