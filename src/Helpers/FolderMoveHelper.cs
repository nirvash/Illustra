using System;
using System.IO;

namespace Illustra.Helpers;

public static class FolderMoveHelper
{
    public static bool CanMove(string sourcePath, string targetFolderPath)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetFolderPath));
        if (Directory.GetParent(source) == null || !Directory.Exists(source) || !Directory.Exists(target))
            return false;
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetDirectoryName(source), target, StringComparison.OrdinalIgnoreCase))
            return false;
        var destination = Path.Combine(target, Path.GetFileName(source));
        return !Directory.Exists(destination) && !File.Exists(destination);
    }

    public static string Move(string sourcePath, string targetFolderPath)
    {
        if (!CanMove(sourcePath, targetFolderPath))
            throw new IOException("このフォルダには移動できません。");
        var destination = Path.Combine(targetFolderPath, Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath)));
        Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(sourcePath, destination, overwrite: false);
        return destination;
    }
}
