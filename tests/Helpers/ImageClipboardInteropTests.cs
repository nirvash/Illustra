using System;
using System.IO;
using System.Threading;
using System.Windows;
using Illustra.Helpers;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Illustra.Tests.Helpers;

[TestFixture]
public class ImageClipboardInteropTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public void CopyImage_StoresBitmap_AndPathCopyStoresText()
    {
        var saved = new DataObject();
        var original = Clipboard.GetDataObject();
        if (original != null)
        {
            foreach (var format in original.GetFormats(false))
            {
                var value = original.GetData(format, false);
                if (value != null) saved.SetData(format, value, false);
            }
        }
        var path = Path.Combine(Path.GetTempPath(), $"illustra-clipboard-{Guid.NewGuid():N}.png");
        try
        {
            using (var image = new Image<Rgba32>(2, 2, new Rgba32(20, 120, 220, 255)))
                image.Save(path);
            ImageClipboardHelper.CopyImageToClipboard(path);
            var copied = Clipboard.GetImage();
            Assert.That(copied, Is.Not.Null);
            Assert.That(copied!.PixelWidth, Is.EqualTo(2));
            Clipboard.SetText(path);
            Assert.That(Clipboard.GetText(), Is.EqualTo(path));
            Assert.That(Clipboard.ContainsImage(), Is.False);
        }
        finally
        {
            Clipboard.SetDataObject(saved, true);
            File.Delete(path);
        }
    }
}
