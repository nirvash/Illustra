using System.Threading.Tasks;

namespace Illustra.Shared.Models
{
    public abstract class McpBaseEventArgs
    {
        public string? SourceId { get; set; } // CS8618 Fix: Allow null
        public TaskCompletionSource<object>? ResultCompletionSource { get; set; } // CS8618 Fix: Allow null
    }
}
