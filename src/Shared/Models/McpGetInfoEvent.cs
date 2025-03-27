using Prism.Events;
using System.Threading;
using System.Threading.Tasks;

namespace Illustra.Shared.Models
{
    public class McpGetInfoEvent : PubSubEvent<McpGetInfoEventArgs> { }

    public class McpGetInfoEventArgs : McpBaseEventArgs
    {
        public string ToolName { get; set; }
        public string FilePath { get; set; }
        public CancellationToken CancellationToken { get; set; }
    }
}
