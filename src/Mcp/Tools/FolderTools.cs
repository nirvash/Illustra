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
        private readonly string _version;

        public FolderTools(IMcpAppBridge bridge)
        {
            _bridge = bridge;
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
