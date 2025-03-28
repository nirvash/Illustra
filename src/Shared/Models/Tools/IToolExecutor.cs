using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Prism.Events;
using Microsoft.Extensions.Logging;

namespace Illustra.Shared.Models.Tools
{
    /// <summary>
    /// Interface for executing an MCP tool.
    /// </summary>
    public interface IToolExecutor
    {
        /// <summary>
        /// Executes the tool asynchronously.
        /// </summary>
        /// <param name="arguments">The arguments for the tool.</param>
        /// <param name="eventAggregator">The event aggregator for publishing results or events.</param>
        /// <param name="logger">The logger for logging execution details.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation. The result object structure depends on the tool.</returns>
        Task<object> ExecuteAsync(JObject arguments, IEventAggregator eventAggregator, ILogger logger, CancellationToken cancellationToken);
    }
}
