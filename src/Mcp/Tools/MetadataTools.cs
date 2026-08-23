using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using Illustra.Helpers;
using Illustra.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Illustra.Mcp.Tools
{
    public record GenerationMetadataInfo(
        [property: JsonPropertyName("generator")] string Generator,
        [property: JsonPropertyName("modelName")] string ModelName,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("negativePrompt")] string NegativePrompt,
        [property: JsonPropertyName("loras")] IReadOnlyList<string> Loras,
        [property: JsonPropertyName("parameters")] IReadOnlyDictionary<string, string> Parameters,
        [property: JsonPropertyName("hasWorkflowJson")] bool HasWorkflowJson);

    public record FileMetadataResult(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("fileName")] string FileName,
        [property: JsonPropertyName("fileSizeBytes")] long FileSizeBytes,
        [property: JsonPropertyName("created")] DateTime Created,
        [property: JsonPropertyName("modified")] DateTime Modified,
        [property: JsonPropertyName("width")] int Width,
        [property: JsonPropertyName("height")] int Height,
        [property: JsonPropertyName("imageFormat")] string ImageFormat,
        [property: JsonPropertyName("rating")] int Rating,
        [property: JsonPropertyName("userComment")] string UserComment,
        [property: JsonPropertyName("generationMetadata")] GenerationMetadataInfo? GenerationMetadata);

    /// <summary>
    /// メタデータ・サムネイル取得ツール。UI スレッド非依存で実行できる。
    /// </summary>
    [McpServerToolType]
    public class MetadataTools
    {
        private const string JpegMimeType = "image/jpeg";
        private const int DefaultThumbnailSize = 512;
        private const int MaxThumbnailSize = 1024;
        private const long MaxImageBytes = 8 * 1024 * 1024;

        private readonly DatabaseManager _db;

        public MetadataTools(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [McpServerTool(Name = "get_file_metadata", ReadOnly = true, Idempotent = true)]
        [Description("Returns metadata of an image/video file: basic info, rating, EXIF user comment and generation metadata (prompt/model/LoRA/parameters for ComfyUI/StableDiffusion generated files).")]
        public async Task<FileMetadataResult> GetFileMetadata(
            [Description("Absolute path of the file.")] string filePath)
        {
            ValidateFile(filePath);

            var fileInfo = new FileInfo(filePath);
            var node = await _db.GetFileNodeAsync(filePath);
            var properties = await ImagePropertiesModel.LoadFromFileAsync(filePath);

            var generation = properties.GenerationMetadata is null
                ? null
                : new GenerationMetadataInfo(
                    properties.GenerationMetadata.Generator,
                    properties.GenerationMetadata.ModelName,
                    properties.GenerationMetadata.Prompt,
                    properties.GenerationMetadata.NegativePrompt,
                    properties.GenerationMetadata.Loras,
                    properties.GenerationMetadata.Parameters,
                    properties.GenerationMetadata.HasWorkflow);

            return new FileMetadataResult(
                filePath,
                properties.FileName ?? fileInfo.Name,
                fileInfo.Length,
                fileInfo.CreationTime,
                fileInfo.LastWriteTime,
                properties.Width,
                properties.Height,
                properties.ImageFormat ?? string.Empty,
                node?.Rating ?? 0,
                properties.UserComment ?? string.Empty,
                generation);
        }

        [McpServerTool(Name = "get_thumbnail", ReadOnly = true, Idempotent = true)]
        [Description("Returns a JPEG thumbnail of the specified image/video file as MCP image content. Use it to visually inspect the image content.")]
        public async Task<IEnumerable<ContentBlock>> GetThumbnail(
            [Description("Absolute path of the file.")] string filePath,
            [Description("Max width/height in pixels (64-1024). Default 512.")] int maxSize = DefaultThumbnailSize)
        {
            ValidateFile(filePath);

            if (maxSize < 64) maxSize = 64;
            if (maxSize > MaxThumbnailSize) maxSize = MaxThumbnailSize;

            var bitmap = await ThumbnailHelper.CreateThumbnailAsync(filePath, maxSize, maxSize);
            if (bitmap == null)
            {
                throw new InvalidOperationException($"Failed to create a thumbnail: {filePath}");
            }

            using var stream = new MemoryStream();
            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder
            {
                QualityLevel = 85
            };
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            var bytes = stream.ToArray();

            if (bytes.Length > MaxImageBytes)
            {
                throw new InvalidOperationException($"Generated thumbnail exceeds {MaxImageBytes / 1024 / 1024}MB limit.");
            }

            return
            [
                ImageContentBlock.FromBytes(bytes, JpegMimeType),
                new TextContentBlock { Text = $"thumbnail of {filePath}" }
            ];
        }

        private static void ValidateFile(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath is required.", nameof(filePath));
            }
            if (!Path.IsPathRooted(filePath))
            {
                throw new ArgumentException("filePath must be an absolute path.", nameof(filePath));
            }
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}", filePath);
            }
        }
    }
}
