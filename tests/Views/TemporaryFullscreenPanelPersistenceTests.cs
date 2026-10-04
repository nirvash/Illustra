using System;
using System.IO;
using NUnit.Framework;

namespace Illustra.Tests.Views;

[TestFixture]
public class TemporaryFullscreenPanelPersistenceTests
{
    [Test]
    public void ExplicitPanelToggle_InTemporaryFullscreen_PersistsVisibilityWithoutWindowSettings()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Illustra.sln")))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Views", "ImageViewerWindow.xaml.cs"));
        var start = source.IndexOf("private void TogglePropertyPanel()", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        var end = source.IndexOf("private void TogglePropertyPanel_Click", start, StringComparison.Ordinal);
        var toggle = source.Substring(start, end - start);
        Assert.That(toggle, Does.Contain("if (IsTemporaryFullscreenHost && !_isInlineHosted)"));
        Assert.That(toggle, Does.Contain("settings.VisiblePropertyPanel = PropertyPanel.Visibility == Visibility.Visible;"));
        Assert.That(toggle, Does.Contain("ViewerSettingsHelper.SaveSettings(settings);"));
        Assert.That(toggle, Does.Not.Contain("settings.IsFullScreen ="));
    }
}
