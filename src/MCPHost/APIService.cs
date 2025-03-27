using Prism.Events;

namespace Illustra.MCPHost
{
    /// <summary>
    /// Handles communication between the Web API and the WPF application logic.
    /// Uses IEventAggregator to publish events that WPF ViewModels/Services subscribe to.
    /// </summary>
    public class APIService
    {
        private readonly IEventAggregator _eventAggregator;

        public APIService(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            // TODO: Add logging if needed
        }

        // TODO: Implement methods like ExecuteToolAsync, GetInfoAsync etc.
        // These methods will typically:
        // 1. Create a TaskCompletionSource<T> for the result.
        // 2. Create an event args object containing parameters and the TCS.
        // 3. Publish the event via _eventAggregator.
        // 4. Await the TCS.Task to get the result from the WPF side.
        // 5. Return the result or throw an exception.
    }
}
