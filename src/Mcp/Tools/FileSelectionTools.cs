using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using Illustra.Events;
using Illustra.Mcp;
using ModelContextProtocol.Server;

namespace Illustra.Mcp.Tools
{
    public record SelectFilesResult(
        [property: JsonPropertyName("selectedCount")] int SelectedCount,
        [property: JsonPropertyName("requestedCount")] int RequestedCount);

    public record SelectedFileInfo(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("fileName")] string FileName);

    public record SelectedFilesResult(
        [property: JsonPropertyName("files")] IReadOnlyList<SelectedFileInfo> Files);

    public record FileListResult(
        [property: JsonPropertyName("folderPath")] string? FolderPath,
        [property: JsonPropertyName("totalCount")] int TotalCount,
        [property: JsonPropertyName("offset")] int Offset,
        [property: JsonPropertyName("returnedCount")] int ReturnedCount,
        [property: JsonPropertyName("files")] IReadOnlyList<FileListItem> Files);

    public record FileListItem(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("fileName")] string FileName,
        [property: JsonPropertyName("fileSize")] long FileSize,
        [property: JsonPropertyName("lastModified")] DateTime LastModified,
        [property: JsonPropertyName("rating")] int Rating);

    /// <summary>
    /// ファイル一覧・選択系ツール。UI 状態を参照するためブリッジ経由で実行する。
    /// </summary>
    [McpServerToolType]
    public class FileSelectionTools
    {
        private readonly IMcpAppBridge _bridge;

        public FileSelectionTools(IMcpAppBridge bridge)
        {
            _bridge = bridge;
        }

        [McpServerTool(Name = "select_file", Destructive = false)]
        [Description("Selects files in the active Illustra folder view. Only files currently loaded in the list can be selected.")]
        public async Task<SelectFilesResult> SelectFile(
            [Description("Absolute paths of the image/video files to select.")] IReadOnlyList<string> paths)
        {
            ValidatePaths(paths);
            var args = new McpSelectFilesEventArgs { Paths = paths };
            var result = await _bridge.PublishAndWaitAsync(args, ea => ea.GetEvent<McpSelectFilesEvent>());
            var selectedCount = result is int count ? count : 0;
            return new SelectFilesResult(selectedCount, paths.Count);
        }

        [McpServerTool(Name = "get_selected_files", ReadOnly = true, Idempotent = true)]
        [Description("Returns the list of files currently selected in the active Illustra folder view.")]
        public async Task<SelectedFilesResult> GetSelectedFiles()
        {
            var args = new McpGetSelectedFilesEventArgs();
            var result = await _bridge.PublishAndWaitAsync(args, ea => ea.GetEvent<McpGetSelectedFilesEvent>());

            if (result is not List<SelectedFileInfoModel> models)
            {
                throw new InvalidOperationException($"Failed to get selected files{(args.ErrorMessage is null ? "" : $": {args.ErrorMessage}")}");
            }

            return new SelectedFilesResult(models
                .Select(m => new SelectedFileInfo(m.Path, m.FileName))
                .ToList());
        }

        [McpServerTool(Name = "list_files", ReadOnly = true, Idempotent = true)]
        [Description("Returns the file list loaded in the active Illustra folder view, including per-file rating.")]
        public async Task<FileListResult> ListFiles(
            [Description("Number of items to skip (pagination). Default 0.")] int offset = 0,
            [Description("Maximum number of items to return. Default 1000.")] int limit = 1000,
            [Description("Minimum rating filter (0-5). -1 means no filter.")] int ratingMin = -1,
            [Description("Maximum rating filter (0-5). -1 means no filter.")] int ratingMax = -1,
            [Description("Extension filter without dot (e.g. \"png\", \"jpg\", \"mp4\"). Case-insensitive.")] string fileType = "")
        {
            if (offset < 0) throw new ArgumentException("offset must be >= 0.", nameof(offset));
            if (limit <= 0 || limit > 5000) throw new ArgumentException("limit must be between 1 and 5000.", nameof(limit));

            var args = new McpGetFileListEventArgs();
            await _bridge.PublishAndWaitAsync(args, ea => ea.GetEvent<McpGetFileListEvent>());

            if (args.Files == null)
            {
                throw new InvalidOperationException($"Failed to get the file list{(args.ErrorMessage is null ? "" : $": {args.ErrorMessage}")}");
            }

            IEnumerable<FileListItem> filtered = args.Files.Select(f =>
                new FileListItem(f.Path, f.FileName, f.FileSize, f.LastModified, f.Rating));

            if (ratingMin >= 0)
            {
                filtered = filtered.Where(f => f.Rating >= ratingMin);
            }
            if (ratingMax >= 0)
            {
                filtered = filtered.Where(f => f.Rating <= ratingMax);
            }
            if (!string.IsNullOrWhiteSpace(fileType))
            {
                var ext = fileType.StartsWith('.') ? fileType : "." + fileType;
                filtered = filtered.Where(f => f.Path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            }

            var materialized = filtered.ToList();
            var page = materialized.Skip(offset).Take(limit).ToList();

            return new FileListResult(args.FolderPath, materialized.Count, offset, page.Count, page);
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
