using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Illustra.MCPHost.Models
{
    /// <summary>
    /// Represents the request body for the /invoke endpoint.
    /// </summary>
    public class InvokeRequest
    {
        /// <summary>
        /// The name of the tool to execute.
        /// </summary>
        [JsonProperty("tool_name")]
        public string? ToolName { get; set; } // CS8618 Fix: Allow null

        /// <summary>
        /// The arguments for the tool, as a JSON object.
        /// Can be JObject for flexibility or a specific type if known.
        /// </summary>
        [JsonProperty("arguments")]
        public JObject? Arguments { get; set; } // CS8618 Fix: Allow null, Using JObject for flexibility
    }
}
