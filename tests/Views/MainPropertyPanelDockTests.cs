using System.IO;
using Illustra.Models;
using NUnit.Framework;

namespace Illustra.Tests.Views;

[TestFixture]
public class MainPropertyPanelDockTests
{
    [Test]
    public void MainPropertyPanelWidth_HasIndependent300PixelDefault()
    {
        var settings = new AppSettingsModel();

        var width = typeof(AppSettingsModel).GetProperty("MainPropertyPanelWidth");

        Assert.That(width, Is.Not.Null);
        Assert.That(width!.GetValue(settings), Is.EqualTo(300));
        Assert.That(settings.PropertySplitterPosition, Is.EqualTo(200), "Legacy height setting remains intact for old settings files.");
    }

    [Test]
    public void SharedMainArea_UsesColumnsForThumbnailAndPropertyPanel()
    {
        var xaml = File.ReadAllText(Path.Combine(ProjectRoot(), "src", "Views", "MainWindow.xaml"));
        var start = xaml.IndexOf("x:Name=\"RightPanelGrid\"", System.StringComparison.Ordinal);
        var end = xaml.IndexOf("</Grid>", start, System.StringComparison.Ordinal);
        var area = xaml.Substring(start, end - start);

        Assert.That(area, Does.Contain("<Grid.ColumnDefinitions>"));
        Assert.That(area, Does.Contain("Grid.Column=\"1\""));
        Assert.That(area, Does.Contain("x:Name=\"PropertySplitter\""));
        Assert.That(area, Does.Contain("<Grid Grid.Column=\"2\""));
        Assert.That(area, Does.Contain("x:Name=\"ThumbnailList\""));
        Assert.That(area, Does.Not.Contain("Grid.Row=\"2\""));
        Assert.That(area, Does.Contain("Width=\"3\""));
        Assert.That(area, Does.Contain("Cursor=\"SizeWE\""));
    }

    [Test]
    public void MainWindow_PersistsAndRestoresPropertyPanelWidthInColumns()
    {
        var source = File.ReadAllText(Path.Combine(ProjectRoot(), "src", "Views", "MainWindow.xaml.cs"));

        Assert.That(source, Does.Contain("RightPanelGrid.ColumnDefinitions[2]"));
        Assert.That(source, Does.Contain("_appSettings.MainPropertyPanelWidth"));
        Assert.That(source, Does.Not.Contain("RightPanelGrid.RowDefinitions[2]"));
        Assert.That(source, Does.Contain("PropertyPanel.SetPresentationEnabled(!isVisible)"));
        Assert.That(source, Does.Contain("_appContext.SetMainPropertyPanelVisible(!isVisible)"));
    }

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Illustra.sln")))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null, "Could not locate the solution root.");
        return directory!.FullName;
    }
}
