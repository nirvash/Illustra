using System.Text.Json.Serialization;

namespace Illustra.Shared.Models
{
    public class ToolExecuteRequest
    {
        [JsonPropertyName("parameters")]
        public object Parameters { get; set; }
    }
}
