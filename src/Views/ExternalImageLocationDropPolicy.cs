using System.IO;
using System.Windows;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Models;
using Prism.Events;

namespace Illustra.Views;

public static class ExternalImageLocationDropPolicy
{
    public static McpOpenFolderEventArgs? CreateNavigationRequest(string imagePath, TabState? tabState = null, bool openInNewTab = false)
    {
        var folderPath = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrWhiteSpace(folderPath)) return null;
        if (!openInNewTab && tabState != null) tabState.FilterSettings = new FilterSettings();
        return new McpOpenFolderEventArgs
        {
            FolderPath = folderPath,
            SelectedFilePath = imagePath,
            OpenInNewTab = openInNewTab
        };
    }

    public static DragDropEffects GetDropEffect(bool accepted, DragDropEffects allowedEffects)
    {
        if (!accepted) return DragDropEffects.None;
        if ((allowedEffects & DragDropEffects.Link) != 0) return DragDropEffects.Link;
        if ((allowedEffects & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
        return DragDropEffects.None;
    }

    public static bool TryCreateDropRequest(string? imagePath, DragDropEffects allowedEffects, TabState? tabState,
        bool openInNewTab, out McpOpenFolderEventArgs? request, out DragDropEffects effect)
    {
        request = null;
        effect = GetDropEffect(!string.IsNullOrWhiteSpace(imagePath), allowedEffects);
        if (effect == DragDropEffects.None || imagePath == null) return false;
        request = CreateNavigationRequest(imagePath, tabState, openInNewTab);
        if (request != null) return true;
        effect = DragDropEffects.None;
        return false;
    }

    public static bool TryCreateDropRequest(string? imagePath, DragDropEffects allowedEffects, TabState? tabState,
        out McpOpenFolderEventArgs? request, out DragDropEffects effect)
        => TryCreateDropRequest(imagePath, allowedEffects, tabState, false, out request, out effect);

    public static void PublishNavigationRequest(IEventAggregator eventAggregator, McpOpenFolderEventArgs request) =>
        eventAggregator.GetEvent<McpOpenFolderEvent>().Publish(request);

    public static bool ShouldDismissDropArea(bool escapePressed, bool mouseButtonsReleased, bool unloaded) =>
        escapePressed || mouseButtonsReleased || unloaded;

    public static bool ShouldDismissAfterHostDragLeave(bool cursorPositionAvailable, Point pointerInHost, Size hostSize)
    {
        if (!cursorPositionAvailable || hostSize.Width <= 0 || hostSize.Height <= 0) return false;
        return pointerInHost.X < 0 || pointerInHost.Y < 0 ||
               pointerInHost.X >= hostSize.Width || pointerInHost.Y >= hostSize.Height;
    }

    public static string? GetFirstSupportedImagePath(IEnumerable<string> paths)
    {
        return paths.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path) &&
            File.Exists(path) &&
            FileHelper.SupportedImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
    }
}
