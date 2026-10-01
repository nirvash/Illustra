namespace Illustra.Mcp
{
    internal static class McpTabTarget
    {
        public static string Normalize(string targetTab)
        {
            return targetTab?.Trim().ToLowerInvariant() switch
            {
                "mcp" => "mcp",
                "active" => "active",
                _ => throw new ArgumentException("targetTab must be mcp or active.", nameof(targetTab))
            };
        }
    }
}
