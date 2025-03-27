using System.Threading.Tasks;

namespace Illustra.Shared.Models
{
    public abstract class McpBaseEventArgs
    {
        public string SourceId { get; set; }
        public TaskCompletionSource<object> ResultCompletionSource { get; set; }
    }
}
