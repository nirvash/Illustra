using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Illustra.Helpers;

/// <summary>
/// Small host-boundary rules shared by the viewer and its WPF runtime tests.
/// </summary>
public static class ViewerHostLogic
{
    public static void MoveSurface(ContentControl source, ContentControl destination)
    {
        if (source.Content is not UIElement surface)
            throw new InvalidOperationException("Viewer surface is not attached to its current host.");

        source.Content = null;
        try
        {
            destination.Content = surface;
        }
        catch
        {
            source.Content = surface;
            throw;
        }
    }

    public static void PrepareDetachedSurface(FrameworkElement surface, object dataContext)
    {
        surface.DataContext = dataContext;
        surface.Focusable = true;
    }

    public static bool FocusDetachedSurface(FrameworkElement surface) => surface.Focus();

    public static string? ResolveCurrentPath(string? requestedPath, string? displayedPath) =>
        string.IsNullOrWhiteSpace(requestedPath) ? displayedPath : requestedPath;

    public static bool IsCurrentOwner(object? viewerOwner, object? activeOwner) =>
        ReferenceEquals(viewerOwner, activeOwner);

    public static bool ShouldPersistWindowSettings(bool isInlineHosted, bool isTemporaryFullscreenHost = false) =>
        !isInlineHosted && !isTemporaryFullscreenHost;

    public static bool ShouldPersistWindowPlacement(bool isTemporaryFullscreenHost) => !isTemporaryFullscreenHost;

    public static void ApplyPropertyPanelLayout(Grid grid, bool isVisible, double panelWidth)
    {
        if (!isVisible || panelWidth <= 0)
        {
            grid.ColumnDefinitions[1].Width = new GridLength(0);
            grid.ColumnDefinitions[2].Width = new GridLength(0);
            return;
        }

        grid.ColumnDefinitions[1].Width = new GridLength(3);
        grid.ColumnDefinitions[2].Width = new GridLength(panelWidth);
    }

    public static bool ShouldAcceptSelection(object? viewerOwner, object? activeOwner) =>
        ReferenceEquals(viewerOwner, activeOwner);

    public static bool ShouldCloseViewerForTabChange(object? viewerOwner, object? newOwner) =>
        !ReferenceEquals(viewerOwner, newOwner);

    public static bool ShouldPersistModeChange(object? viewerOwner, object? activeOwner, bool hasViewer) =>
        !hasViewer || ReferenceEquals(viewerOwner, activeOwner);

    public static bool IsDoubleClick(int clickCount, MouseButton button, bool originatingControlIsMedia) =>
        originatingControlIsMedia && button == MouseButton.Left && clickCount == 2;

    public static bool TryHandleInlineEscape(Key key, bool inlineViewerActive, Action closeViewer)
    {
        if (key != Key.Escape || !inlineViewerActive)
            return false;

        closeViewer();
        return true;
    }
}
