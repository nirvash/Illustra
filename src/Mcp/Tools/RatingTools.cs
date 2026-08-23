using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Models;
using ModelContextProtocol.Server;
using Prism.Events;

namespace Illustra.Mcp.Tools
{
    public record SetRatingResult(
        [property: JsonPropertyName("processed")] IReadOnlyList<string> Processed,
        [property: JsonPropertyName("processedCount")] int ProcessedCount,
        [property: JsonPropertyName("requestedCount")] int RequestedCount,
        [property: JsonPropertyName("rating")] int Rating,
        [property: JsonPropertyName("failedCount")] int FailedCount,
        [property: JsonPropertyName("failed")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<FileOperationFailure>? Failed = null);

    /// <summary>
    /// レーティング変更ツール。DB へ直接永続化し（未表示ファイルにも対応）、
    /// 表示中のビューへは RatingChangedEvent で即時反映する。
    /// </summary>
    [McpServerToolType]
    public class RatingTools
    {
        private readonly DatabaseManager _db;
        private readonly IEventAggregator _eventAggregator;

        public RatingTools(DatabaseManager db, IEventAggregator eventAggregator)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        }

        [McpServerTool(Name = "set_files_rating", Idempotent = true)]
        [Description("Sets the rating (0-5, 0 clears the rating) of files. Open Illustra views reflect the change immediately.")]
        public async Task<SetRatingResult> SetFilesRating(
            [Description("Absolute paths of the files to rate.")] IReadOnlyList<string> paths,
            [Description("New rating value from 0 to 5. 0 clears the existing rating.")] int rating)
        {
            ValidatePaths(paths);
            if (rating is < 0 or > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(rating), "rating must be between 0 and 5.");
            }

            var processed = new List<string>();
            var failures = new List<FileOperationFailure>();
            foreach (var path in paths)
            {
                try
                {
                    await ApplyRatingAsync(path, rating);
                    processed.Add(path);
                    PublishRatingChanged(path, rating);
                }
                catch (Exception ex)
                {
                    LogHelper.LogError($"MCP set_files_rating: {path} のレーティング変更に失敗しました: {ex.Message}");
                    // エージェントが再試行/代替手段を判断できるよう、失敗理由をそのまま返す
                    failures.Add(new FileOperationFailure(path, ex.GetBaseException().Message));
                }
            }

            return new SetRatingResult(processed, processed.Count, paths.Count, rating, failures.Count, failures);
        }

        private async Task ApplyRatingAsync(string path, int rating)
        {
            // 未登録のファイルにレーティングを付けた場合は新規ノードを作る。
            // UpdateRatingAsync は Upsert（更新0行なら Insert）で動くため、
            // ファイルパス付きコンストラクタで FileType/FileSize 等の
            // NOT NULL 列とメタデータを初期化しておく。
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"File not found: {fullPath}", fullPath);
            }

            var node = await _db.GetFileNodeAsync(fullPath) ?? new FileNodeModel(fullPath);

            node.Rating = rating;
            await _db.UpdateRatingAsync(node);
        }

        private void PublishRatingChanged(string path, int rating)
        {
            _eventAggregator.GetEvent<RatingChangedEvent>().Publish(
                new RatingChangedEventArgs { FilePath = path, Rating = rating });
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
