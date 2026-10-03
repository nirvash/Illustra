using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Illustra.Helpers
{
    public static class ImageClipboardHelper
    {
        public static BitmapSource LoadBitmapSource(string path)
        {
            using var image = Image.Load<Bgra32>(path);
            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96,
                PixelFormats.Bgra32, null, pixels, image.Width * 4);
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