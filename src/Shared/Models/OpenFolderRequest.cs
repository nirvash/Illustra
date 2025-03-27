using System.Text.Json.Serialization;

namespace Illustra.Shared.Models
{
    public class OpenFolderRequest
    {
        [JsonPropertyName("folderPath")]
        public string FolderPath { get; set; }
    }
}
