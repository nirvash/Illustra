using System;
using System.Threading.Tasks;

namespace Illustra.Services
{
    /// <summary>
    /// Provides an abstraction for dispatching operations to the UI thread.
    /// </summary>
    public interface IDispatcherService
    {
        /// <summary>
        /// Asynchronously executes the specified action on the UI thread.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task InvokeAsync(Action action);

        // Add other necessary Dispatcher methods if needed (e.g., BeginInvoke, CheckAccess)
    }
}
