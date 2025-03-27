using Prism.Events;
using System.Threading.Tasks;

namespace Illustra.Shared.Models
{
    public class McpOpenFolderEvent : PubSubEvent<McpOpenFolderEventArgs> { }

    public class McpOpenFolderEventArgs : McpBaseEventArgs
    {
        public string FolderPath { get; set; }
        public string? SelectedFilePath { get; set; } // Optional file to select after opening
    }
}
