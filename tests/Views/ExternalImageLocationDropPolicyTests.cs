using System;
using System.IO;
using System.Linq;
using System.Windows;
using Illustra.Events;
using Illustra.Models;
using Illustra.Views;
using NUnit.Framework;
using Prism.Events;

namespace Illustra.Tests.Views;

[TestFixture]
public class ExternalImageLocationDropPolicyTests
{
    [Test]
    public void ReturnsFirstExistingSupportedImage_AndSkipsOtherFiles()
    {
        var unsupported = Path.GetTempFileName();
        var image = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        File.WriteAllBytes(image, Array.Empty<byte>());

        try
        {
            Assert.That(ExternalImageLocationDropPolicy.GetFirstSupportedImagePath(
                new[] { unsupported, image, image }), Is.EqualTo(image));
            Assert.That(ExternalImageLocationDropPolicy.GetFirstSupportedImagePath(new[] { unsupported }), Is.Null);
        }
        finally
        {
            File.Delete(unsupported);
            File.Delete(image);
        }
    }

    [Test]
    public void CreatesNormalFolderNavigationRequestSelectingTheDroppedImage()
    {
        var state = new TabState
        {
            FilterSettings = new FilterSettings { Rating = 4, HasPrompt = true, Tags = { "tag" }, Extensions = { ".jpg" } }
        };
        var request = ExternalImageLocationDropPolicy.CreateNavigationRequest(@"C:\images\sample.png", state);

        Assert.That(request, Is.Not.Null);
        Assert.That(request!.FolderPath, Is.EqualTo(@"C:\images"));
        Assert.That(request.SelectedFilePath, Is.EqualTo(@"C:\images\sample.png"));
        Assert.That(request.SourceId, Is.Null);
        Assert.That(state.FilterSettings.IsAnyFilterActive, Is.False);
    }

    [Test]
    public void NewTabRequestPreservesCurrentTabFilterAndMarksOnlyNewTabNavigation()
    {
        var state = new TabState
        {
            FilterSettings = new FilterSettings { Rating = 4, HasPrompt = true, Tags = { "tag" } },
            SelectedItemPath = @"C:\old\selected.png"
        };

        var request = ExternalImageLocationDropPolicy.CreateNavigationRequest(@"C:\images\sample.png", state, true);

        Assert.Multiple(() =>
        {
            Assert.That(request!.OpenInNewTab, Is.True);
            Assert.That(request.FolderPath, Is.EqualTo(@"C:\images"));
            Assert.That(request.SelectedFilePath, Is.EqualTo(@"C:\images\sample.png"));
            Assert.That(state.FilterSettings.Rating, Is.EqualTo(4));
            Assert.That(state.FilterSettings.HasPrompt, Is.True);
            Assert.That(state.FilterSettings.Tags, Does.Contain("tag"));
            Assert.That(state.SelectedItemPath, Is.EqualTo(@"C:\old\selected.png"));
        });
    }

    [TestCase(DragDropEffects.Copy, DragDropEffects.Copy)]
    [TestCase(DragDropEffects.Link, DragDropEffects.Link)]
    [TestCase(DragDropEffects.Copy | DragDropEffects.Link, DragDropEffects.Link)]
    [TestCase(DragDropEffects.Move, DragDropEffects.None)]
    [TestCase(DragDropEffects.None, DragDropEffects.None)]
    public void ChoosesOnlyAnAllowedNonDestructiveDropEffect(DragDropEffects allowed, DragDropEffects expected)
    {
        Assert.That(ExternalImageLocationDropPolicy.GetDropEffect(true, allowed), Is.EqualTo(expected));
    }

    [Test]
    public void DisallowedDropDoesNotChangeTabFilterOrCreateNavigationRequest()
    {
        var state = new TabState { FilterSettings = new FilterSettings { Rating = 3 } };

        var accepted = ExternalImageLocationDropPolicy.TryCreateDropRequest(
            @"C:\images\sample.png", DragDropEffects.Move, state, out var request, out var effect);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(request, Is.Null);
            Assert.That(effect, Is.EqualTo(DragDropEffects.None));
            Assert.That(state.FilterSettings.Rating, Is.EqualTo(3));
        });
    }

    [Test]
    public void PublishesNormalOpenFolderEventWithImageSelection()
    {
        var aggregator = new EventAggregator();
        McpOpenFolderEventArgs? received = null;
        aggregator.GetEvent<McpOpenFolderEvent>().Subscribe(args => received = args);
        var request = ExternalImageLocationDropPolicy.CreateNavigationRequest(@"C:\images\sample.png");

        ExternalImageLocationDropPolicy.PublishNavigationRequest(aggregator, request!);

        Assert.That(received, Is.SameAs(request));
        Assert.That(received!.FolderPath, Is.EqualTo(@"C:\images"));
        Assert.That(received.SelectedFilePath, Is.EqualTo(@"C:\images\sample.png"));
        Assert.That(received.SourceId, Is.Null);
    }

    [Test]
    public void DropAreaCleanupDismissesWhenMouseButtonsAreReleased()
    {
        Assert.That(ExternalImageLocationDropPolicy.ShouldDismissDropArea(false, true, false), Is.True);
    }

    [Test]
    [TestCase(534.0, 220.0, false)] // ListView descendant transition.
    [TestCase(600.0, 80.0, false)] // Overlay child transition.
    [TestCase(-1.0, 80.0, true)] // Pointer left the stable host.
    [TestCase(1200.0, 80.0, true)]
    public void DragLeaveDismissalUsesCurrentPointerInSharedHost(double pointerX, double pointerY, bool expectedDismissal)
    {
        Assert.That(ExternalImageLocationDropPolicy.ShouldDismissAfterHostDragLeave(
            true, new Point(pointerX, pointerY), new Size(1200, 600)), Is.EqualTo(expectedDismissal));
    }

    [Test]
    public void DragLeaveKeepsDropAreaWhenCurrentPointerOrHostGeometryIsUnavailable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExternalImageLocationDropPolicy.ShouldDismissAfterHostDragLeave(false, new Point(-100, -100), new Size(1200, 600)), Is.False);
            Assert.That(ExternalImageLocationDropPolicy.ShouldDismissAfterHostDragLeave(true, new Point(-100, -100), new Size(0, 0)), Is.False);
        });
    }

    [Test]
    public void BothDragLeaveHandlersUseCurrentSharedHostPointerInsteadOfLeaveCoordinates()
    {
        var source = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", "Views", "ThumbnailListControl.xaml.cs"));
        var listLeaveStart = source.IndexOf("private void ThumbnailItemsControl_ExternalFileDragLeave", StringComparison.Ordinal);
        var listLeaveEnd = source.IndexOf("private bool TryGetCurrentPointerInDropHost", listLeaveStart, StringComparison.Ordinal);
        var listLeave = source.Substring(listLeaveStart, listLeaveEnd - listLeaveStart);
        var overlayLeaveStart = source.IndexOf("private void OpenImageLocationDropArea_DragLeave", StringComparison.Ordinal);
        var overlayLeaveEnd = source.IndexOf("private void OpenImageLocationDropArea_Drop", overlayLeaveStart, StringComparison.Ordinal);
        var overlayLeave = source.Substring(overlayLeaveStart, overlayLeaveEnd - overlayLeaveStart);

        Assert.Multiple(() =>
        {
            Assert.That(listLeave, Does.Contain("TryGetCurrentPointerInDropHost"));
            Assert.That(listLeave, Does.Not.Contain("e.GetPosition"));
            Assert.That(overlayLeave, Does.Contain("TryGetCurrentPointerInDropHost"));
            Assert.That(overlayLeave, Does.Not.Contain("e.GetPosition"));
        });
    }

    [TestCase(true, false, false, true)]
    [TestCase(false, true, false, true)]
    [TestCase(false, false, true, true)]
    [TestCase(false, false, false, false)]
    public void DropAreaCleanupHandlesEscapeAndUnload(bool escapePressed, bool mouseReleased, bool unloaded, bool expected)
    {
        Assert.That(ExternalImageLocationDropPolicy.ShouldDismissDropArea(escapePressed, mouseReleased, unloaded), Is.EqualTo(expected));
    }
    [Test]
    public void EscapeAndUnloadCleanupAreWiredToActiveDropMonitorAndDropHandler()
    {
        var source = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", "Views", "ThumbnailListControl.xaml.cs"));
        var tickStart = source.IndexOf("_externalDropMonitor.Tick +=", StringComparison.Ordinal);
        var tickEnd = source.IndexOf("};", tickStart, StringComparison.Ordinal);
        var tick = source.Substring(tickStart, tickEnd - tickStart);
        var dropStart = source.IndexOf("private void OpenImageLocationDropArea_Drop", StringComparison.Ordinal);
        var dropEnd = source.IndexOf("private void HideImageLocationDropArea", dropStart, StringComparison.Ordinal);
        var drop = source.Substring(dropStart, dropEnd - dropStart);

        Assert.Multiple(() =>
        {
            Assert.That(tick, Does.Contain("GetAsyncKeyState(0x1B)"));
            Assert.That(tick, Does.Not.Contain("Mouse.LeftButton"));
            Assert.That(tick, Does.Contain("GetAsyncKeyState(0x01)"));
            Assert.That(tick, Does.Contain("GetAsyncKeyState(0x02)"));
            Assert.That(tick, Does.Contain("ShouldDismissDropArea(escapePressed, mouseButtonsReleased, !IsLoaded)"));
            Assert.That(source, Does.Contain("if (show) _externalDropMonitor.Start()"));
            Assert.That(source, Does.Contain("else _externalDropMonitor.Stop()"));
            Assert.That(source, Does.Contain("_externalDropMonitor?.Stop()"));
            Assert.That(source, Does.Contain("OpenImageLocationDropArea.Visibility = Visibility.Collapsed"));
            Assert.That(drop, Does.Contain("TryCreateDropRequest(imagePath, e.AllowedEffects, state, openInNewTab"));
            Assert.That(drop, Does.Contain("PublishNavigationRequest(_eventAggregator, request!)"));
        });
    }

    [Test]
    public void GongHandledEventsAreObservedWithoutChangingNormalDropRouting()
    {
        var source = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", "Views", "ThumbnailListControl.xaml.cs"));
        var observerStart = source.IndexOf("private void UpdateLocationDropArea", StringComparison.Ordinal);
        var observerEnd = source.IndexOf("private void ThumbnailItemsControl_ExternalFileDragLeave", observerStart, StringComparison.Ordinal);
        var observer = source.Substring(observerStart, observerEnd - observerStart);
        var registration = source.Substring(source.IndexOf("ThumbnailItemsControl.AddHandler(DragEnterEvent", StringComparison.Ordinal), 800);
        var parentRegistrationStart = source.IndexOf("OpenImageLocationDropArea.AddHandler(DragEnterEvent", StringComparison.Ordinal);
        var parentRegistration = source.Substring(parentRegistrationStart, 500);

        Assert.Multiple(() =>
        {
            Assert.That(registration, Does.Contain("new DragEventHandler(ThumbnailItemsControl_ExternalFileDragEnter), true"));
            Assert.That(registration, Does.Contain("new DragEventHandler(ThumbnailItemsControl_ExternalFileDragOver), true"));
            Assert.That(registration, Does.Contain("new DragEventHandler(ThumbnailItemsControl_DropCleanup), true"));
            Assert.That(parentRegistration, Does.Contain("new DragEventHandler(OpenImageLocationDropArea_ParentDragEnter), true"));
            Assert.That(parentRegistration, Does.Contain("new DragEventHandler(OpenImageLocationDropArea_ParentDragLeave), true"));
            Assert.That(observer, Does.Contain("ExternalImageDropTrace.Write(eventName, sender, e, OpenImageLocationDropArea"));
            Assert.That(observer, Does.Not.Contain("e.Effects"));
            Assert.That(observer, Does.Not.Contain("e.Handled"));
        });
    }

    [Test]
    public void DropOverlayOffersCurrentAndNewTabTargets()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Views", "ThumbnailListControl.xaml"));
        var policy = File.ReadAllText(Path.Combine(root, "src", "Views", "ExternalImageLocationDropPolicy.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("OpenImageLocationCurrentTabDropArea"));
            Assert.That(xaml, Does.Contain("OpenImageLocationNewTabDropArea"));
            Assert.That(policy, Does.Contain("OpenInNewTab"));
            Assert.That(xaml, Does.Contain("String_Thumbnail_OpenImageLocationCurrentTab"));
            Assert.That(xaml, Does.Contain("String_Thumbnail_OpenImageLocationNewTab"));
        });
    }

    [Test]
    public void NormalFolderNavigationSynchronizesTabAndFilterThroughExistingPath()
    {
        var source = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", "ViewModels", "MainWindowViewModel.cs"));
        var thumbnailSource = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", "Views", "ThumbnailListControl.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("HandleFolderSelected(args.FolderPath, args.SelectedFilePath)"));
            Assert.That(source, Does.Contain("SelectedTab.State.FolderPath = path"));
            Assert.That(source, Does.Contain("SelectedTab.State.SelectedItemPath = filePath"));
            Assert.That(source, Does.Contain("new SelectedTabChangedEventArgs(SelectedTab.State)"));
            Assert.That(source, Does.Contain("filter => filter.SourceId != CONTROL_ID && filter.SourceId != \"MainWindow\""));
            Assert.That(thumbnailSource, Does.Contain("_viewModel.ClearAllFilters()"));
            Assert.That(thumbnailSource, Does.Contain("LoadFileNodesAsync(folderPath, selectedFilePath, filterSettings, sortSettings"));
            Assert.That(thumbnailSource, Does.Contain("SetFullUpdate(filterSettings)"));
        });
    }

    private static string FindProjectRoot()
    {
        foreach (var startPath in new[] { Environment.GetEnvironmentVariable("ILLUSTRA_PROJECT_ROOT"),
                     TestContext.CurrentContext.TestDirectory, Environment.CurrentDirectory }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var directory = new DirectoryInfo(startPath);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Illustra.sln")))
                directory = directory.Parent;
            if (directory != null) return directory.FullName;
        }
        Assert.Fail("Illustra.sln was not found from the test directory or current directory.");
        return string.Empty;
    }
}
