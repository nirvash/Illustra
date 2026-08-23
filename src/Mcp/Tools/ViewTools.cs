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
    /// アクティブビューのフィルタ操作ツール。UI 状態を変更するためブリッジ経由で実行する。
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
        [Description("Changes the filter of the active Illustra folder view. Specify at least one option, or clear=true to remove all filters. ratingMin shows files rated at or above the value (0 disables the rating filter). Use get_app_status to read the current filter state.")]
        public async Task<SetViewFilterResult> SetViewFilter(
            [Description("Enable or disable the AI-generation-prompt filter.")] bool? promptFilter = null,
            [Description("Minimum rating to show (0-5). 0 disables the rating filter.")] int? ratingMin = null,
            [Description("File extensions to show without dot (e.g. \"png\", \"mp4\"). Enables the extension filter when specified.")] IReadOnlyList<string>? extensions = null,
            [Description("When true, removes all filters and ignores other options.")] bool clear = false)
        {
            if (!clear && !promptFilter.HasValue && !ratingMin.HasValue && (extensions == null || extensions.Count == 0))
            {
                throw new ArgumentException("Specify at least one filter option (promptFilter, ratingMin, extensions) or set clear=true.");
            }
            if (ratingMin.HasValue && ratingMin.Value is < 0 or > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(ratingMin), "ratingMin must be between 0 and 5.");
            }

            var args = new McpSetViewFilterEventArgs
            {
                Clear = clear,
                PromptFilterEnabled = promptFilter,
                RatingMin = ratingMin,
                Extensions = extensions?.ToList()
            };

            var result = await _bridge.PublishAndWaitAsync(args, ea => ea.GetEvent<McpSetViewFilterEvent>());

            if (result is not true)
            {
                throw new InvalidOperationException($"Failed to apply the view filter{(args.ErrorMessage is null ? "" : $": {args.ErrorMessage}")}");
            }

            var applied = args.AppliedFilterState ?? new ViewFilterStateModel();
            return new SetViewFilterResult(true, new ViewFilterState(
                applied.RatingMin,
                applied.IsPromptFilterEnabled,
                applied.IsTagFilterEnabled,
                applied.TagFilters,
                applied.IsExtensionFilterEnabled,
                applied.ExtensionFilters));
        }
    }
}
