using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Illustra.Helpers;
using NUnit.Framework;
using SkiaSharp;

namespace Illustra.Tests.Helpers;

[TestFixture]
public class SkiaImageDecoderTests
{
    private string _path;
    [SetUp] public void SetUp() => _path = Path.Combine(Path.GetTempPath(), $"illustra-skia-{Guid.NewGuid():N}.png");
    [TearDown] public void TearDown() { if (File.Exists(_path)) File.Delete(_path); }

    private SKBitmap Decode() => SkiaImageDecoder.DecodeFirstFrame(_path);

    [Test]
    public void DecodeFirstFrame_PreservesStraightBgraAndExactAlpha()
    {
        byte[] rgba = { 20, 120, 220, 0, 20, 120, 220, 1, 20, 120, 220, 254, 20, 120, 220, 255 };
        File.WriteAllBytes(_path, TestPngBuilder.BuildRgbaPng(4, 1, rgba));
        using var bitmap = Decode();
        Assert.That(bitmap.AlphaType, Is.EqualTo(SKAlphaType.Unpremul));
        Assert.That(bitmap.ColorType, Is.EqualTo(SKColorType.Bgra8888));
        var pixels = new byte[16];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        Assert.That(pixels, Is.EqualTo(new byte[] { 220, 120, 20, 0, 220, 120, 20, 1, 220, 120, 20, 254, 220, 120, 20, 255 }));

        using var codec = SKCodec.Create(_path);
        using var premul = new SKBitmap(new SKImageInfo(4, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        Assert.That(codec.GetPixels(premul.Info, premul.GetPixels()), Is.EqualTo(SKCodecResult.Success));
        Marshal.Copy(premul.GetPixels(), pixels, 0, pixels.Length);
        Assert.That(pixels[0], Is.Zero, "premultiplication destroys transparent RGB");
        Assert.That(pixels[4], Is.LessThan(3), "premultiplied bytes cannot be labeled Bgra32");
    }

    [Test]
    public void LoadBitmapSource_UsesStraightBgra32()
    {
        File.WriteAllBytes(_path, TestPngBuilder.BuildRgbaPng(2, 1, new byte[] { 20, 120, 220, 1, 20, 120, 220, 254 }));
        var bitmap = ImageClipboardHelper.LoadBitmapSource(_path);
        Assert.That(bitmap.Format, Is.EqualTo(PixelFormats.Bgra32));
        var pixels = new byte[8];
        bitmap.CopyPixels(pixels, 8, 0);
        Assert.That(pixels, Is.EqualTo(new byte[] { 220, 120, 20, 1, 220, 120, 20, 254 }));
    }

    [Test]
    public void DecodeFirstFrame_ApngReturnsFirstFrameOnly()
    {
        File.WriteAllBytes(_path, TestPngBuilder.BuildTwoFrameApng("first"));
        using var bitmap = Decode();
        Assert.That(bitmap.GetPixel(0, 0), Is.EqualTo(new SKColor(255, 0, 0, 255)));
    }

    [TestCase(0)]
    [TestCase(254)]
    public void ParseStealthPng_PreservesAlphaLsbAtBothExtremes(byte alphaBase)
    {
        File.WriteAllBytes(_path, TestPngBuilder.BuildStealthPng("{\"Software\":\"NovelAI\",\"Description\":\"a peaceful landscape\"}", alphaBase: alphaBase));
        Assert.That(NovelAIMetadataParser.ParseStealthPng(_path)?.Prompt, Is.EqualTo("a peaceful landscape"));
    }

    [Test]
    public void DecodeFirstFrame_RejectsNonImageTruncatedAndOversizedInput()
    {
        File.WriteAllText(_path, "not an image");
        Assert.Throws<InvalidDataException>(() => Decode());
        byte[] png = TestPngBuilder.BuildRgbaPng(1, 1, new byte[] { 0, 0, 0, 255 });
        File.WriteAllBytes(_path, png[..29]);
        Assert.Throws<InvalidDataException>(() => Decode());
        Assert.That(NovelAIMetadataParser.ParseStealthPng(_path), Is.Null);
        File.WriteAllBytes(_path, TestPngBuilder.BuildPngWithDeclaredSize(8192, 8192));
        Assert.Throws<InvalidDataException>(() => Decode());
        Assert.That(NovelAIMetadataParser.ParseStealthPng(_path), Is.Null);
        using (var stream = File.Create(_path)) stream.SetLength(128L * 1024 * 1024 + 1);
        Assert.Throws<InvalidDataException>(() => Decode());
        Assert.That(NovelAIMetadataParser.ParseStealthPng(_path), Is.Null);
    }

    [TestCase(".png")]
    [TestCase(".webp")]
    public void LoadBitmapSource_TransparentImagePreservesAlpha(string extension)
    {
        _path = Path.ChangeExtension(_path, extension);
        byte[] rgba = { 20, 120, 220, 0, 20, 120, 220, 1, 20, 120, 220, 254, 20, 120, 220, 255 };
        byte[] png = TestPngBuilder.BuildRgbaPng(4, 1, rgba);
        if (extension == ".webp")
        {
            using var image = SKImage.FromEncodedData(png);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 100);
            File.WriteAllBytes(_path, encoded.ToArray());
        }
        else File.WriteAllBytes(_path, png);
        var bitmap = ImageClipboardHelper.LoadBitmapSource(_path);
        var pixels = new byte[16];
        bitmap.CopyPixels(pixels, 16, 0);
        Assert.That(new byte[] { pixels[3], pixels[7], pixels[11], pixels[15] }, Is.EqualTo(new byte[] { 0, 1, 254, 255 }));
        Assert.That(pixels[4], Is.GreaterThan(200), "near-transparent blue must remain straight, not premultiplied");
    }
}
