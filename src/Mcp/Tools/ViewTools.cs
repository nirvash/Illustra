using System.ComponentModel;
using System.Text.Json.Serialization;
using Illustra.Events;
using Illustra.Mcp;
using ModelContextProtocol.Server;

namespace Illustra.Mcp.Tools
{
    public record SetViewFilterResult(
        [property: JsonPropertyName("applied")] bool Applied,
        [property: JsonPropertyName("filterState")] ViewFilterState FilterState);

    /// <summary>
    /// アクティブビューのフィルタ操作ツール。UI 状況を変更するためブリッジ経路で実行する。
    /// </summary>
    [McpServerToolType]
    public class ViewTools
    {
        private readonly IMcpAppBridge _bridge;

        public ViewTools(IMcpAppBridge bridge)
        {
            _bridge = bridge;
        }

        [McpServerTool(Name = "set_view_filter", Idempotent = true)]
        [Description("Changes the filter of the active Illustra folder view. Specify at least one option, or clear=true to remove all filters. rating shows only files rated exactly at the value (0 disables the rating filter). Use get_app_status to read the current filter state.")]
        public async Task<SetViewFilterResult> SetViewFilter(
            [Description("Enable or disable the AI-generation-prompt filter.")] bool? promptFilter = null,
            [Description("Shows only files rated exactly at this value (1-5). 0 disables the rating filter.")] int? rating = null,
            [Description("File extensions to show, with or without a leading dot (e.g. \"png\", \".mp4\"). Enables the extension filter when specified. Pass an empty list to disable the extension filter only.")] IReadOnlyList<string>? extensions = null,
            [Description("When true, removes all filters and ignores other options.")] bool clear = false)
        {
            if (!clear && !promptFilter.HasValue && !rating.HasValue && extensions == null)
            {
                throw new ArgumentException("Specify at least one filter option (promptFilter, rating, extensions) or set clear=true.");
            }
            if (rating.HasValue && rating.Value is < 0 or > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(rating), "rating must be between 0 and 5.");
            }

            var normalizedExtensions = extensions?
                .Select(e => e?.Trim())
                .Where(e => !string.IsNullOrEmpty(e))
                .Select(e => "." + e.TrimStart('.').ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var args = new McpSetViewFilterEventArgs
            {
                Clear = clear,
                PromptFilterEnabled = promptFilter,
                Rating = rating,
                Extensions = normalizedExtensions
            };

            var result = await _bridge.PublishAndWaitAsync(args, ea => ea.GetEvent<McpSetViewFilterEvent>());

            if (result is not true)
            {
                throw new InvalidOperationException($"Failed to apply the view filter{(args.ErrorMessage is null ? "" : $": {args.ErrorMessage}")}");
            }

            var applied = args.AppliedFilterState ?? new ViewFilterStateModel();
            return new SetViewFilterResult(true, new ViewFilterState(
                applied.Rating,
                applied.IsPromptFilterEnabled,
                applied.IsTagFilterEnabled,
                applied.TagFilters,
                applied.IsExtensionFilterEnabled,
                applied.ExtensionFilters));
        }
    }
}

