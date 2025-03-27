using Prism.Events;
using System.Threading;
using System.Threading.Tasks;

namespace Illustra.Shared.Models
{
    public class McpExecuteToolEvent : PubSubEvent<McpExecuteToolEventArgs> { }

    public class McpExecuteToolEventArgs : McpBaseEventArgs
    {
        public string ToolName { get; set; }
        public object Parameters { get; set; }
        public CancellationToken CancellationToken { get; set; }
    }
}
