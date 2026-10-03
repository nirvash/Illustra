using System;
using System.IO;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Illustra.Helpers;
using NUnit.Framework;

namespace Illustra.Tests.Views;

[TestFixture]
public class ViewerHostModeTests
{
    private sealed class SyntheticViewerContext : INotifyPropertyChanged
    {
        public BitmapSource? ImageSource { get; set; }
        public SyntheticProperties Properties { get; } = new();
        public event PropertyChangedEventHandler? PropertyChanged;
        public void NotifyImageChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageSource)));
    }

    private sealed class SyntheticProperties
    {
        public int Rating { get; set; } = 4;
    }

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Illustra.sln")))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null);
        return directory!.FullName;
    }

    [Test]
    public void InlineFullscreenHandoff_RehostsSameLiveSurfaceWithoutDisposingOrReloading()
    {
        var source = File.ReadAllText(Path.Combine(ProjectRoot(), "src", "Views", "ThumbnailListControl.xaml.cs"));
        var start = source.IndexOf("internal void OpenSeparateViewerForInlineFullscreen", StringComparison.Ordinal);
        var end = source.IndexOf("internal void ReturnInlineAfterFullscreen", start, StringComparison.Ordinal);
        var handoff = source.Substring(start, end - start);
        var transition = handoff.Substring(0, handoff.IndexOf("catch (Exception ex)", StringComparison.Ordinal));
        var returnStart = end;
        var returnEnd = source.IndexOf("private void BackToThumbnailsButton_Click", returnStart, StringComparison.Ordinal);
        var returnHandoff = source.Substring(returnStart, returnEnd - returnStart);
        Assert.Multiple(() =>
        {
            Assert.That(transition, Does.Contain("ViewerHostLogic.MoveSurface(InlineViewerHost, viewer)"));
            Assert.That(transition, Does.Contain("viewer.AttachSurfaceToWindowHost()"));
            Assert.That(transition.IndexOf("viewer.PrepareTemporaryFullscreenHost", StringComparison.Ordinal), Is.LessThan(transition.IndexOf("viewer.Show()", StringComparison.Ordinal)));
            Assert.That(transition, Does.Not.Contain("ToggleFullScreenFromHost"));
            Assert.That(transition, Does.Not.Contain("CloseInlineViewer"));
            Assert.That(transition, Does.Not.Contain("LoadContentFromPath"));
            Assert.That(transition, Does.Not.Contain("new ImageViewerWindow"));
            Assert.That(returnHandoff, Does.Contain("viewer.DetachSurfaceForInlineHost()"));
            Assert.That(returnHandoff, Does.Not.Contain("ShowInlineViewer(filePath)"));
            Assert.That(returnHandoff, Does.Not.Contain("viewer.Close()"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void TemporaryFullscreenPreparation_ArmsInlineReturnBeforeEnteringFullscreen()
    {
        var source = File.ReadAllText(Path.Combine(ProjectRoot(), "src", "Views", "ImageViewerWindow.xaml.cs"));
        var start = source.IndexOf("public void PrepareTemporaryFullscreenHost", StringComparison.Ordinal);
        var end = source.IndexOf("private void RestoreInlinePropertyPanel", start, StringComparison.Ordinal);
        var preparation = source.Substring(start, end - start);
        var arm = preparation.IndexOf("_returnToInlineAfterFullscreenExit = true;", StringComparison.Ordinal);
        Assert.That(arm, Is.GreaterThanOrEqualTo(0), "The prepared fullscreen route must arm its inline return callback.");
        Assert.That(arm, Is.LessThan(preparation.IndexOf("IsFullScreen = true;", StringComparison.Ordinal)));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void RehostedSurface_RemainsTheSameVisibleBoundMediaUntilReturned()
    {
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 3, 2, 1, 255 }, 4);
        source.Freeze();
        var context = new SyntheticViewerContext { ImageSource = source };
        var image = new Image { Width = 1, Height = 1 };
        BindingOperations.SetBinding(image, Image.SourceProperty, new Binding("ImageSource"));
        var surface = new Grid { DataContext = context, Visibility = Visibility.Visible };
        surface.Children.Add(image);
        var inlineHost = new ContentControl();
        var windowHost = new Window { Width = 40, Height = 40, ShowInTaskbar = false };
        inlineHost.Content = surface;
        windowHost.Show();
        try
        {
            ViewerHostLogic.MoveSurface(inlineHost, windowHost);
            windowHost.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            BindingOperations.GetBindingExpression(image, Image.SourceProperty)!.UpdateTarget();
            Assert.Multiple(() =>
            {
                Assert.That(inlineHost.Content, Is.Null);
                Assert.That(windowHost.Content, Is.SameAs(surface));
                Assert.That(image.Source, Is.SameAs(source));
                Assert.That(surface.Visibility, Is.EqualTo(Visibility.Visible));
            });

            ViewerHostLogic.MoveSurface(windowHost, inlineHost);
            Assert.Multiple(() =>
            {
                Assert.That(windowHost.Content, Is.Null);
                Assert.That(inlineHost.Content, Is.SameAs(surface));
                Assert.That(image.Source, Is.SameAs(source));
            });
        }
        finally
        {
            windowHost.Close();
        }
    }

    [Test]
    public void InlineCloseAndFailedFullscreenHandoff_RestoreThumbnailVisibility()
    {
        var source = File.ReadAllText(Path.Combine(ProjectRoot(), "src", "Views", "ThumbnailListControl.xaml.cs"));
        var closeStart = source.IndexOf("internal void CloseInlineViewer(", StringComparison.Ordinal);
        var closeEnd = source.IndexOf("internal void OnInlineViewerClosed", closeStart, StringComparison.Ordinal);
        var close = source.Substring(closeStart, closeEnd - closeStart);
        var openStart = source.IndexOf("internal void OpenSeparateViewerForInlineFullscreen", StringComparison.Ordinal);
        var openEnd = source.IndexOf("internal void ReturnInlineAfterFullscreen", openStart, StringComparison.Ordinal);
        var open = source.Substring(openStart, openEnd - openStart);
        Assert.Multiple(() =>
        {
            Assert.That(close, Does.Contain("if (restoreThumbnails) ThumbnailItemsControl.Visibility = Visibility.Visible;"));
            Assert.That(open, Does.Contain("catch (Exception ex)"));
            Assert.That(open, Does.Contain("CloseInlineViewer();"));
            Assert.That(source, Does.Contain("if (!ReferenceEquals(_imageViewerWindow, viewer)) return;"));
        });
    }

    [Test]
    public void ViewerMode_PersistsSeparateWindowAsBackwardCompatibleDefault()
    {
        var source = File.ReadAllText(Path.Combine(ProjectRoot(), "src", "Helpers", "ViewerSettingsHelper.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("enum ViewerDisplayMode"));
            Assert.That(source, Does.Contain("ViewerDisplayMode DisplayMode { get; set; } = ViewerDisplayMode.SeparateWindow"));
        });
    }

    [Test]
    public void MediaControls_SeparateTransferUnloadFromFinalDisposal()
    {
        var root = ProjectRoot();
        var webp = File.ReadAllText(Path.Combine(root, "src", "Controls", "WebpPlayerControl.xaml.cs"));
        var video = File.ReadAllText(Path.Combine(root, "src", "Controls", "VideoPlayerControl.xaml.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(webp, Does.Contain("BeginHostTransfer"));
            Assert.That(webp, Does.Contain("CompleteHostTransfer"));
            Assert.That(webp, Does.Contain("DisposeForFinalClose"));
            Assert.That(video, Does.Contain("BeginHostTransfer"));
            Assert.That(video, Does.Contain("CompleteHostTransfer"));
            Assert.That(video, Does.Contain("DisposeForFinalClose"));
            Assert.That(video, Does.Contain("Position"));
        });
    }

    [Test]
    public void TemporaryFullscreenClose_DoesNotPersistViewerOrMahAppsPlacementSettings()
    {
        var root = ProjectRoot();
        var helper = File.ReadAllText(Path.Combine(root, "src", "Helpers", "ViewerHostLogic.cs"));
        var viewer = File.ReadAllText(Path.Combine(root, "src", "Views", "ImageViewerWindow.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Views", "ImageViewerWindow.xaml"));
        Assert.Multiple(() =>
        {
            Assert.That(helper, Does.Contain("isTemporaryFullscreenHost"));
            Assert.That(viewer, Does.Contain("ShouldPersistWindowSettings(_isInlineHosted, IsTemporaryFullscreenHost)"));
            Assert.That(viewer, Does.Contain("ShouldPersistWindowPlacement(IsTemporaryFullscreenHost)"));
            Assert.That(xaml, Does.Contain("SaveWindowPosition=\"True\""));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void RealMediaControls_ReparentUnloadPreservesLifetimeAndFinalCloseDisposes()
    {
        var app = Application.Current ?? new Application();
        app.Resources["BooleanToVisibilityConverter"] = new System.Windows.Controls.BooleanToVisibilityConverter();
        var webp = new Illustra.Controls.WebpPlayerControl();
        var video = new Illustra.Controls.VideoPlayerControl();
        var firstHost = new Window { Width = 80, Height = 80, ShowInTaskbar = false };
        var secondHost = new Window { Width = 80, Height = 80, ShowInTaskbar = false };
        firstHost.Content = new StackPanel { Children = { webp, video } };
        firstHost.Show();
        secondHost.Show();
        try
        {
            firstHost.UpdateLayout();
            var webpUnloaded = 0;
            var videoUnloaded = 0;
            var webpLoaded = 0;
            var videoLoaded = 0;
            webp.Unloaded += (_, _) => webpUnloaded++;
            video.Unloaded += (_, _) => videoUnloaded++;
            webp.Loaded += (_, _) => webpLoaded++;
            video.Loaded += (_, _) => videoLoaded++;
            webp.BeginHostTransfer();
            video.BeginHostTransfer();
            var surface = firstHost.Content;
            firstHost.Content = null;
            firstHost.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            Assert.Multiple(() =>
            {
                Assert.That(webpUnloaded, Is.GreaterThan(0), "the real WebP control must actually unload during the transfer gap");
                Assert.That(videoUnloaded, Is.GreaterThan(0), "the real video control must actually unload during the transfer gap");
                Assert.That(GetLifetime(webp).IsDisposed, Is.False);
                Assert.That(GetLifetime(video).IsDisposed, Is.False);
                Assert.That(GetLifetime(webp).IsTransferPending, Is.True);
                Assert.That(GetLifetime(video).IsTransferPending, Is.True);
            });

            secondHost.Content = surface;
            secondHost.UpdateLayout();
            secondHost.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            Assert.Multiple(() =>
            {
                Assert.That(webpLoaded, Is.GreaterThan(0), "the real WebP control must load in the destination host");
                Assert.That(videoLoaded, Is.GreaterThan(0), "the real video control must load in the destination host");
                Assert.That(GetLifetime(webp).IsDisposed, Is.False);
                Assert.That(GetLifetime(video).IsDisposed, Is.False);
                Assert.That(GetLifetime(webp).IsTransferPending, Is.False);
                Assert.That(GetLifetime(video).IsTransferPending, Is.False);
                Assert.That(secondHost.Content, Is.SameAs(surface));
            });

            secondHost.Content = null;
            secondHost.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            Assert.Multiple(() =>
            {
                Assert.That(GetLifetime(webp).IsDisposed, Is.True);
                Assert.That(GetLifetime(video).IsDisposed, Is.True);
            });
            webp.DisposeForFinalClose();
            video.DisposeForFinalClose();
            Assert.Multiple(() =>
            {
                Assert.That(GetLifetime(webp).IsDisposed, Is.True);
                Assert.That(GetLifetime(video).IsDisposed, Is.True);
            });
        }
        finally
        {
            firstHost.Close();
            secondHost.Close();
        }
    }

    private static ViewerSurfaceLifetime GetLifetime(object control)
    {
        var field = control.GetType().GetField("_surfaceLifetime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (ViewerSurfaceLifetime)field!.GetValue(control)!;
    }

    [Test]
    public void ThumbnailToolbar_UsesIconActionsAndNoOverlayBackButton()
    {
        var root = ProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Views", "ThumbnailListControl.xaml"));
        var english = File.ReadAllText(Path.Combine(root, "src", "Resources", "Strings.xaml"));
        var japanese = File.ReadAllText(Path.Combine(root, "src", "Resources", "Strings.ja.xaml"));
        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("ToggleViewerModeButton_Click"));
            Assert.That(xaml, Does.Contain("String_Viewer_ModeToggleTooltip"));
            Assert.That(xaml, Does.Not.Contain("InlineBackButton"));
            Assert.That(xaml, Does.Contain("x:Name=\"BackToThumbnailsButton\""));
            Assert.That(xaml, Does.Contain("x:Name=\"InlineFullscreenButton\""));
            Assert.That(File.ReadAllText(Path.Combine(root, "src", "Views", "ThumbnailListControl.xaml.cs")), Does.Not.Contain("ToggleViewerModeButton.Content"));
            Assert.That(File.ReadAllText(Path.Combine(root, "src", "Views", "ThumbnailListControl.xaml.cs")), Does.Contain("OpenSeparateViewerForInlineFullscreen(_imageViewerWindow?.RequestedFilePath)"));
            var viewerXaml = File.ReadAllText(Path.Combine(root, "src", "Views", "ImageViewerWindow.xaml"));
            var viewerCode = File.ReadAllText(Path.Combine(root, "src", "Views", "ImageViewerWindow.xaml.cs"));
            Assert.That(viewerXaml, Does.Contain("DockInline_Click"));
            Assert.That(viewerCode, Does.Contain("Parent.DockSeparateViewerToInline(this)"));
            Assert.That(File.ReadAllText(Path.Combine(root, "src", "Views", "ThumbnailListControl.xaml.cs")), Does.Contain("SwitchViewerMode(ViewerDisplayMode.Inline)"));
            Assert.That(english, Does.Contain("String_Viewer_ModeToggleTooltip"));
            Assert.That(japanese, Does.Contain("String_Viewer_ModeToggleTooltip"));
        });
    }

    [Test]
    public void ViewerRouting_TracksOwningTabAndClosesInlineWithoutClosingMainWindow()
    {
        var source = File.ReadAllText(Path.Combine(ProjectRoot(), "src", "Views", "ThumbnailListControl.xaml.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("InlineViewerHost"));
            Assert.That(source, Does.Contain("CloseInlineViewer"));
            Assert.That(source, Does.Contain("_viewerTabState"));
            Assert.That(source, Does.Contain("ViewerDisplayMode.Inline"));
            Assert.That(source, Does.Contain("TryHandleInlineEscape"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void DetachedSurface_BindsImageAndRatingFromViewerDataContext()
    {
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 1, 2, 3, 255 }, 4);
        source.Freeze();
        var context = new SyntheticViewerContext { ImageSource = source };
        var surface = new Grid();
        var image = new Image { Source = null };
        var rating = new TextBlock();
        surface.Children.Add(image);
        surface.Children.Add(rating);
        BindingOperations.SetBinding(image, Image.SourceProperty, new Binding("ImageSource"));
        BindingOperations.SetBinding(rating, TextBlock.TextProperty, new Binding("Properties.Rating"));

        ViewerHostLogic.PrepareDetachedSurface(surface, context);
        using var host = new TestWindowHost(surface);
        surface.UpdateLayout();
        BindingOperations.GetBindingExpression(image, Image.SourceProperty)!.UpdateTarget();
        BindingOperations.GetBindingExpression(rating, TextBlock.TextProperty)!.UpdateTarget();

        Assert.Multiple(() =>
        {
            Assert.That(surface.DataContext, Is.SameAs(context));
            Assert.That(surface.Focusable, Is.True);
            Assert.That(image.Source, Is.SameAs(source));
            Assert.That(rating.Text, Is.EqualTo("4"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void InlineEscape_ClosesExactlyOnceAndOnlyForActiveInlineHost()
    {
        var closeCount = 0;
        var root = new Grid();
        var surface = new Grid { Focusable = true };
        root.Children.Add(surface);
        bool routedHandled = false;
        root.PreviewKeyDown += (_, e) =>
        {
            if (ViewerHostLogic.TryHandleInlineEscape(e.Key, true, () => closeCount++))
            {
                e.Handled = true;
                routedHandled = true;
            }
        };
        using var host = new TestWindowHost(root);
        Keyboard.Focus(surface);
        Keyboard.ClearFocus();
        Assert.That(ViewerHostLogic.FocusDetachedSurface(surface), Is.True);
        var keyEvent = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(surface)!, 0, Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        surface.RaiseEvent(keyEvent);

        Assert.Multiple(() =>
        {
            Assert.That(Keyboard.FocusedElement, Is.SameAs(surface));
            Assert.That(routedHandled, Is.True);
            Assert.That(keyEvent.Handled, Is.True);
            Assert.That(closeCount, Is.EqualTo(1));
            Assert.That(ViewerHostLogic.TryHandleInlineEscape(Key.Escape, false, () => closeCount++), Is.False);
            Assert.That(ViewerHostLogic.TryHandleInlineEscape(Key.Space, true, () => closeCount++), Is.False);
            Assert.That(closeCount, Is.EqualTo(1));
        });
    }

    private sealed class TestWindowHost : IDisposable
    {
        private readonly Window _window;
        public TestWindowHost(UIElement content)
        {
            _window = new Window { Content = content, Width = 40, Height = 40, ShowInTaskbar = false };
            _window.Show();
        }
        public void Dispose() => _window.Close();
    }

    [Test]
    public void InlineHostSettingsAndOwnershipRules_PreserveWindowSettingsAndRequestedPath()
    {
        var firstOwner = new object();
        var otherOwner = new object();
        Assert.Multiple(() =>
        {
            Assert.That(ViewerHostLogic.ShouldPersistWindowSettings(isInlineHosted: true), Is.False);
            Assert.That(ViewerHostLogic.ShouldPersistWindowSettings(isInlineHosted: false), Is.True);
            Assert.That(ViewerHostLogic.ShouldPersistWindowSettings(isInlineHosted: false, isTemporaryFullscreenHost: true), Is.False);
            Assert.That(ViewerHostLogic.ShouldPersistWindowPlacement(isTemporaryFullscreenHost: true), Is.False);
            Assert.That(ViewerHostLogic.ShouldPersistWindowPlacement(isTemporaryFullscreenHost: false), Is.True);
            Assert.That(ViewerHostLogic.IsCurrentOwner(firstOwner, firstOwner), Is.True);
            Assert.That(ViewerHostLogic.IsCurrentOwner(firstOwner, otherOwner), Is.False);
            Assert.That(ViewerHostLogic.ShouldAcceptSelection(firstOwner, firstOwner), Is.True);
            Assert.That(ViewerHostLogic.ShouldAcceptSelection(firstOwner, otherOwner), Is.False);
            Assert.That(ViewerHostLogic.ShouldCloseViewerForTabChange(firstOwner, otherOwner), Is.True);
            Assert.That(ViewerHostLogic.ShouldCloseViewerForTabChange(firstOwner, firstOwner), Is.False);
            Assert.That(ViewerHostLogic.ShouldPersistModeChange(firstOwner, otherOwner, hasViewer: true), Is.False);
            Assert.That(ViewerHostLogic.ShouldPersistModeChange(firstOwner, otherOwner, hasViewer: false), Is.True);
            Assert.That(ViewerHostLogic.ResolveCurrentPath("requested.png", null), Is.EqualTo("requested.png"));
            Assert.That(ViewerHostLogic.ResolveCurrentPath(null, "displayed.png"), Is.EqualTo("displayed.png"));
            Assert.That(ViewerHostLogic.ResolveCurrentPath(" ", "displayed.png"), Is.EqualTo("displayed.png"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void InlineMediaDoubleClick_UsesBubblingLeftButtonClickCountWithoutInterceptingControls()
    {
        var root = new Grid();
        var media = new Border();
        root.Children.Add(media);
        var closeCount = 0;
        root.AddHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (ViewerHostLogic.IsDoubleClick(e.ClickCount, e.ChangedButton, ReferenceEquals(e.OriginalSource, media)))
            {
                closeCount++;
                e.Handled = true;
            }
        }));
        using var host = new TestWindowHost(root);
        var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent
        };
        media.RaiseEvent(down);
        Assert.That(closeCount, Is.Zero);

        var doubleDown = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent
        };
        // ClickCount is provided by WPF input; the pure policy is exercised for the actual double-click value.
        Assert.That(ViewerHostLogic.IsDoubleClick(2, MouseButton.Left, true), Is.True);
        Assert.That(ViewerHostLogic.IsDoubleClick(2, MouseButton.Right, true), Is.False);
        Assert.That(ViewerHostLogic.IsDoubleClick(2, MouseButton.Left, false), Is.False);
        Assert.That(ViewerHostLogic.IsDoubleClick(1, MouseButton.Left, true), Is.False);
        Assert.That(doubleDown.ClickCount, Is.EqualTo(1));
        Assert.That(closeCount, Is.Zero);
    }
}
