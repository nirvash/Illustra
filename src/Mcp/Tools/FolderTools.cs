using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Mcp;
using ModelContextProtocol.Server;

namespace Illustra.Mcp.Tools
{
    public record FolderToolResult(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("path")] string Path);

    public record FavoriteFolderInfo(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("displayName")] string? DisplayName);

    public record FavoriteFoldersResult(
        [property: JsonPropertyName("folders")] IReadOnlyList<FavoriteFolderInfo> Folders);

    public record CreateFolderResult(
        [property: JsonPropertyName("created")] bool Created,
        [property: JsonPropertyName("alreadyExisted")] bool AlreadyExisted,
        [property: JsonPropertyName("path")] string Path);

    public record RenameFolderResult(
        [property: JsonPropertyName("renamed")] bool Renamed,
        [property: JsonPropertyName("oldPath")] string OldPath,
        [property: JsonPropertyName("newPath")] string NewPath,
        [property: JsonPropertyName("databaseUpdated")] bool DatabaseUpdated);

    public record ServerInfoResult(
        [property: JsonPropertyName("serverName")] string ServerName,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("endpoint")] string Endpoint);

    /// <summary>
    /// フォルダ操作系ツール。
    /// </summary>
    [McpServerToolType]
    public class FolderTools
    {
        private readonly IMcpAppBridge _bridge;
        private readonly DatabaseManager _db;
        private readonly string _version;

        public FolderTools(IMcpAppBridge bridge, DatabaseManager db)
        {
            _bridge = bridge;
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _version = typeof(FolderTools).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        [McpServerTool(Name = "open_folder", Idempotent = true)]
        [Description("Opens the specified folder in the Illustra application and optionally selects a file.")]
        public async Task<FolderToolResult> OpenFolder(
            [Description("Absolute path of the folder to open.")] string folderPath,
            [Description("Optional absolute path of a file to select after opening the folder.")] string? selectedFilePath = null)
        {
            ValidatePath(folderPath, nameof(folderPath), requireDirectory: true);
            if (!string.IsNullOrEmpty(selectedFilePath) && !File.Exists(selectedFilePath))
            {
                throw new FileNotFoundException($"File not found: {selectedFilePath}", selectedFilePath);
            }

            var args = new McpOpenFolderEventArgs
            {
                FolderPath = folderPath,
                SelectedFilePath = selectedFilePath
            };
            var result = await _bridge.PublishAndWaitAsync(args, ea => ea.GetEvent<McpOpenFolderEvent>());

            if (result is not true)
            {
                throw new InvalidOperationException($"Illustra failed to open the folder: {folderPath}");
            }

            return new FolderToolResult(true, folderPath);
        }

        [McpServerTool(Name = "get_favorite_folders", ReadOnly = true, Idempotent = true)]
        [Description("Returns the list of folders registered as favorites in Illustra.")]
        public FavoriteFoldersResult GetFavoriteFolders()
        {
            var favorites = SettingsHelper.GetSettings().FavoriteFolders?
                .Where(f => !string.IsNullOrEmpty(f.Path))
                .Select(f => new FavoriteFolderInfo(f.Path!, f.DisplayName))
                .ToList() ?? [];

            return new FavoriteFoldersResult(favorites);
        }

        [McpServerTool(Name = "create_folder")]
        [Description("Creates a folder. Returns alreadyExisted=true without error if the folder exists.")]
        public CreateFolderResult CreateFolder(
            [Description("Absolute path of the folder to create. Parent folders are created as needed.")] string path)
        {
            ValidatePath(path, nameof(path));

            var existed = Directory.Exists(path);
            if (!existed)
            {
                Directory.CreateDirectory(path);
            }

            return new CreateFolderResult(!existed, existed, path);
        }

        [McpServerTool(Name = "rename_folder", Destructive = true)]
        [Description("Renames a folder in the file system. Database entries of contained files follow the rename. Open views and the folder tree update automatically via the file system monitor.")]
        public async Task<RenameFolderResult> RenameFolder(
            [Description("Absolute path of the folder to rename.")] string folderPath,
            [Description("New folder name only, without path separators. Must not already exist in the parent folder.")] string newFolderName)
        {
            ValidatePath(folderPath, nameof(folderPath), requireDirectory: true);

            // ドライブルート (D:\ 等) は親を持たないためリネーム不可
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
            if (fullPath.Equals(Path.GetPathRoot(fullPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Cannot rename a drive root: {fullPath}", nameof(folderPath));
            }

            if (string.IsNullOrWhiteSpace(newFolderName) ||
                newFolderName.IndexOfAny(['/', '\\', ':']) >= 0 ||
                Path.GetInvalidFileNameChars().Any(newFolderName.Contains))
            {
                throw new ArgumentException("newFolderName must be a plain folder name without path separators or invalid characters.", nameof(newFolderName));
            }

            var parent = Path.GetDirectoryName(fullPath)!;
            var newPath = Path.Combine(parent, newFolderName);
            if (!string.Equals(fullPath, newPath, StringComparison.OrdinalIgnoreCase) &&
                (Directory.Exists(newPath) || File.Exists(newPath)))
            {
                throw new IOException($"Target already exists: {newPath}");
            }

            Directory.Move(fullPath, newPath);

            // DB パス更新は一時的な IO 競合などを考慮してリトライする。
            // 全試行が失敗した場合はロールバックせず、結果で databaseUpdated=false を返す。
            const int maxAttempts = 3;
            var dbUpdated = false;
            for (var attempt = 1; attempt <= maxAttempts && !dbUpdated; attempt++)
            {
                try
                {
                    await _db.UpdateFolderPathsAsync(fullPath, newPath);
                    dbUpdated = true;
                }
                catch (Exception ex)
                {
                    LogHelper.LogError($"MCP rename_folder: DB パス更新に失敗しました ({attempt}/{maxAttempts}) ({fullPath} -> {newPath}): {ex.Message}");
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(500 * attempt);
                    }
                }
            }

            return new RenameFolderResult(true, fullPath, newPath, dbUpdated);
        }

        [McpServerTool(Name = "get_server_info", ReadOnly = true, Idempotent = true)]
        [Description("Returns information about the Illustra MCP server.")]
        public ServerInfoResult GetServerInfo()
        {
            return new ServerInfoResult("Illustra", _version, "/mcp");
        }

        private static void ValidatePath(string? path, string paramName, bool requireDirectory = false)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException($"{paramName} is required.", paramName);
            }
            if (!Path.IsPathRooted(path))
            {
                throw new ArgumentException($"{paramName} must be an absolute path.", paramName);
            }
            if (requireDirectory && !Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"Folder not found: {path}");
            }
        }
    }
}
