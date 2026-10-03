using Illustra.Models;
using Illustra.Views;
using NUnit.Framework;

namespace Illustra.Tests.Views;

[TestFixture]
public class FileSystemTreePathCopyTests
{
    [TestCase(@"C:\Pictures\folder", true)]
    [TestCase(@"C:\Pictures\image.png", false)]
    public void CopyTreeItemFullPath_CopiesExactItemPath(string fullPath, bool isFolder)
    {
        var item = new FileSystemItemModel(fullPath, isFolder, true);
        string? copiedPath = null;

        var copied = FileSystemTreeView.CopyTreeItemFullPath(item, path => copiedPath = path);

        Assert.That(copied, Is.True);
        Assert.That(copiedPath, Is.EqualTo(fullPath));
    }

    [Test]
    public void CopyTreeItemFullPath_DoesNotCopyWhenItemIsMissing()
    {
        var copyInvoked = false;

        var copied = FileSystemTreeView.CopyTreeItemFullPath(null, _ => copyInvoked = true);

        Assert.That(copied, Is.False);
        Assert.That(copyInvoked, Is.False);
    }
}
