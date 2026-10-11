using System.IO;
using SkiaSharp;

namespace Illustra.Tests.Helpers;

internal static class TestImageFixture
{
    public static void WriteSolidImage(string path)
    {
        using var bitmap = new SKBitmap(2, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        bitmap.Erase(new SKColor(20, 120, 220, 255));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(Path.GetExtension(path) == ".webp"
            ? SKEncodedImageFormat.Webp : SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        encoded.SaveTo(file);
    }
}
