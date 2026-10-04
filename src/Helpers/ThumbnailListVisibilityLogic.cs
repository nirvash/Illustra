using System.Windows;

namespace Illustra.Helpers;

/// <summary>Resolves thumbnail visibility from folder loading and viewer host state.</summary>
public static class ThumbnailListVisibilityLogic
{
    public static Visibility Resolve(bool isFolderLoading, bool isInlineViewerActive, bool isTemporaryFullscreenHost)
    {
        if (isInlineViewerActive || isTemporaryFullscreenHost)
            return Visibility.Collapsed;
        return isFolderLoading ? Visibility.Hidden : Visibility.Visible;
    }
}
