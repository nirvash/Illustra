using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using Illustra.Helpers;
using ModelContextProtocol.Server;

namespace Illustra.Mcp.Tools
{
    /// <summary>
    /// ファイル操作で失敗したファイルの情報。
    /// </summary>
    public record FileOperationFailure(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("error")] string Error);

    public record FileOperationResult(
        [property: JsonPropertyName("processed")] IReadOnlyList<string> Processed,
        [property: JsonPropertyName("processedCount")] int ProcessedCount,
        [property: JsonPropertyName("requestedCount")] int RequestedCount,
        [property: JsonPropertyName("failedCount")] int FailedCount,
        [property: JsonPropertyName("failed")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<FileOperationFailure>? Failed = null);

    public record RenameResult(
        [property: JsonPropertyName("renamed")] bool Renamed,
        [property: JsonPropertyName("oldPath")] string OldPath,
        [property: JsonPropertyName("newPath")] string NewPath);

    /// <summary>
    /// ファイル移動・コピー系ツール。FileOperationHelper 経由で実行し、DB の整合性（レーティング引き継ぎ等）を維持する。
    /// </summary>
    [McpServerToolType]
    public class FileOperationTools
    {
        private readonly DatabaseManager _db;

        public FileOperationTools(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [McpServerTool(Name = "move_files")]
        [Description("Moves files to an existing target folder. Ratings are preserved. Files with duplicate names are renamed like \"name (1).ext\".")]
        public Task<FileOperationResult> MoveFiles(
            [Description("Absolute paths of the files to move.")] IReadOnlyList<string> paths,
            [Description("Absolute path of the destination folder, which must already exist. Use create_folder first if needed.")] string targetFolder)
            => ExecuteAsync(paths, targetFolder, isCopy: false);

        [McpServerTool(Name = "copy_files", Idempotent = true)]
        [Description("Copies files to an existing target folder. Files with duplicate names are renamed like \"name (1).ext\".")]
        public Task<FileOperationResult> CopyFiles(
            [Description("Absolute paths of the files to copy.")] IReadOnlyList<string> paths,
            [Description("Absolute path of the destination folder, which must already exist. Use create_folder first if needed.")] string targetFolder)
            => ExecuteAsync(paths, targetFolder, isCopy: true);

        [McpServerTool(Name = "delete_files", Destructive = true)]
        [Description("Deletes files and removes their database entries (rating etc.). Files are moved to the recycle bin by default so they can be restored. Set permanent=true only when explicitly requested.")]
        public async Task<FileOperationResult> DeleteFiles(
            [Description("Absolute paths of the files to delete.")] IReadOnlyList<string> paths,
            [Description("When true, permanently deletes instead of sending to the recycle bin.")] bool permanent = false)
        {
            ValidatePaths(paths);

            var fileOperation = new FileOperationHelper(_db);
            var processed = new List<string>();
            var failures = new List<FileOperationFailure>();
            foreach (var path in paths)
            {
                try
                {
                    await fileOperation.DeleteFileQuietAsync(path, useRecycleBin: !permanent);
                    processed.Add(path);
                }
                catch (Exception ex)
                {
                    LogHelper.LogError($"MCP delete_files: {path} の削除に失敗しました: {ex.Message}");
                    // エージェントが再試行/代替手段を判断できるよう、失敗理由をそのまま返す
                    failures.Add(new FileOperationFailure(path, ex.GetBaseException().Message));
                }
            }

            return new FileOperationResult(processed, processed.Count, paths.Count, failures.Count, failures);
        }

        [McpServerTool(Name = "rename_file", Destructive = true)]
        [Description("Renames a single file within its current folder. The database entry (rating etc.) follows the rename. Fails if the target name already exists.")]
        public async Task<RenameResult> RenameFile(
            [Description("Absolute path of the file to rename.")] string filePath,
            [Description("New file name including its extension. Must not contain path separators.")] string newFileName)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath is required.", nameof(filePath));
            }
            if (!Path.IsPathRooted(filePath))
            {
                throw new ArgumentException($"filePath must be an absolute path: {filePath}", nameof(filePath));
            }
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}", filePath);
            }
            if (string.IsNullOrWhiteSpace(newFileName) ||
                newFileName.IndexOfAny(['/', '\\', ':']) >= 0)
            {
                throw new ArgumentException("newFileName must be a plain file name without path separators.", nameof(newFileName));
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
            var newPath = Path.Combine(directory, newFileName);

            if (!string.Equals(Path.GetFullPath(filePath), newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(newPath))
            {
                throw new IOException($"Target file already exists: {newPath}");
            }

            var fileOperation = new FileOperationHelper(_db);
            await fileOperation.RenameFile(Path.GetFullPath(filePath), newPath);

            return new RenameResult(true, filePath, newPath);
        }

        private async Task<FileOperationResult> ExecuteAsync(IReadOnlyList<string>? paths, string? targetFolder, bool isCopy)
        {
            var operationName = isCopy ? "copy" : "move";

            if (paths == null || paths.Count == 0)
            {
                throw new ArgumentException("paths is required and must contain at least one entry.", nameof(paths));
            }
            if (string.IsNullOrWhiteSpace(targetFolder))
            {
                throw new ArgumentException("targetFolder is required.", nameof(targetFolder));
            }
            if (!Path.IsPathRooted(targetFolder))
            {
                throw new ArgumentException("targetFolder must be an absolute path.", nameof(targetFolder));
            }
            if (!Directory.Exists(targetFolder))
            {
                throw new DirectoryNotFoundException($"Target folder does not exist: {targetFolder}. Create it with create_folder first.");
            }

            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path))
                {
                    throw new FileNotFoundException($"File not found: {path}", path);
                }
            }

            // 移動元と移動先が同一フォルダの場合は意味がないため拒否
            if (!isCopy)
            {
                var sameFolder = paths.All(p =>
                    string.Equals(
                        Path.GetDirectoryName(Path.GetFullPath(p)),
                        Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase));
                if (sameFolder)
                {
                    throw new InvalidOperationException($"Source and target folders are the same: {targetFolder}");
                }
            }

            var fileOperation = new FileOperationHelper(_db);
            var processed = await fileOperation.ExecuteFileOperation(
                paths.ToList(),
                Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar),
                isCopy);

            return new FileOperationResult(processed, processed.Count, paths.Count, paths.Count - processed.Count);
        }

        private static void ValidatePaths(IReadOnlyList<string>? paths)
        {
            if (paths == null || paths.Count == 0)
            {
                throw new ArgumentException("paths is required and must contain at least one entry.", nameof(paths));
            }
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new ArgumentException("paths contains an empty entry.", nameof(paths));
                }
                if (!Path.IsPathRooted(path))
                {
                    throw new ArgumentException($"paths must contain absolute paths: {path}", nameof(paths));
                }
            }
        }
    }
}
