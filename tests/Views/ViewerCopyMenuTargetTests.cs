using System.IO;
using NUnit.Framework;

namespace Illustra.Tests.Views;

[TestFixture]
public class ViewerCopyMenuTargetTests
{
    private static string ReadViewerSource()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Illustra.sln")))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null);
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Views", "ImageViewerWindow.xaml.cs"));
    }

    [Test]
    public void EveryMediaSurface_OpensCopyMenu()
    {
        var source = ReadViewerSource();
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("ImageZoomControl.PreviewMouseRightButtonDown += Media_MouseRightButtonDown;"));
            Assert.That(source, Does.Contain("WebpPlayer.PreviewMouseRightButtonDown += Media_MouseRightButtonDown;"));
            Assert.That(source, Does.Contain("VideoPlayerControl.PreviewMouseRightButtonDown += Media_MouseRightButtonDown;"));
        });
    }

    [Test]
    public void CopyMenu_CapturesDisplayedPathAndNeverUsesNavigationPath()
    {
        var source = ReadViewerSource();
        var start = source.IndexOf("private void ShowPromptMenu(");
        var end = source.IndexOf("private enum PromptCopyType", start);
        var menu = source.Substring(start, end - start);
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("var targetPath = _displayedFilePath;"));
            Assert.That(menu, Does.Not.Contain("_currentFilePath"));
            Assert.That(menu, Does.Contain("Clipboard.SetText(targetPath)"));
            Assert.That(menu, Does.Contain("CopyImageToClipboard(targetPath)"));
            Assert.That(menu, Does.Contain("new[] { targetPath }"));
            Assert.That(menu, Does.Contain("menu.PlacementTarget = placementTarget;"));
        });
    }
}
