using System.IO;
using System;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Illustra.Helpers;

namespace Illustra.Tests.Helpers
{
    [TestFixture]
    public class ImageClipboardHelperTests
    {
        [TestCase(".png")]
        [TestCase(".webp")]
        public void LoadBitmapSource_DecodesImageAndReleasesFile(string extension)
        {
            var path = Path.Combine(Path.GetTempPath(), $"illustra-clipboard-{Guid.NewGuid():N}{extension}");
            try
            {
                using (var image = new Image<Rgba32>(2, 2, new Rgba32(20, 120, 220, 255)))
                {
                    image.Save(path);
                }

                var bitmap = ImageClipboardHelper.LoadBitmapSource(path);

                Assert.That(bitmap, Is.InstanceOf<BitmapSource>());
                Assert.That(bitmap.IsFrozen, Is.True);
                Assert.That(bitmap.PixelWidth, Is.EqualTo(2));
                Assert.That(bitmap.PixelHeight, Is.EqualTo(2));
                var pixels = new byte[16];
                bitmap.CopyPixels(pixels, 8, 0);
                Assert.That(pixels[0], Is.EqualTo(220).Within(3));
                Assert.That(pixels[1], Is.EqualTo(120).Within(3));
                Assert.That(pixels[2], Is.EqualTo(20).Within(3));
                Assert.That(pixels[3], Is.EqualTo(255));
                File.Delete(path);
                Assert.That(File.Exists(path), Is.False);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
