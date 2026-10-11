using System;
using System.IO;
using System.Threading;
using System.Windows;
using Illustra.Helpers;
using NUnit.Framework;

namespace Illustra.Tests.Helpers;

[TestFixture]
public class ImageClipboardInteropTests
{
    [Test]
    [Explicit("実クリップボードを操作するため、明示した手動検証でのみ実行する。")]
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
            TestImageFixture.WriteSolidImage(path);
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
