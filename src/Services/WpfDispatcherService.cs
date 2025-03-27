using System;
using System.Threading.Tasks;
using System.Windows; // Required for Application.Current
using System.Windows.Threading; // Required for Dispatcher

namespace Illustra.Services
{
    /// <summary>
    /// Implements IDispatcherService using the WPF Dispatcher.
    /// </summary>
    public class WpfDispatcherService : IDispatcherService
    {
        private readonly Dispatcher _dispatcher;

        public WpfDispatcherService()
        {
            // Ensure this is called from the UI thread during application startup
            _dispatcher = Application.Current.Dispatcher;
        }

        /// <summary>
        /// Asynchronously executes the specified action on the UI thread.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task InvokeAsync(Action action)
        {
            if (_dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }
            else
            {
                return _dispatcher.InvokeAsync(action).Task;
            }
        }

        // Implement other IDispatcherService methods if added
    }
}
