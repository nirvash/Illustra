using Illustra.Shared.Attributes;
using Prism.Events;
using System.Text.Json.Serialization; // For OpenFolderRequest if using System.Text.Json
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using System; // For ArgumentException
// using Illustra.Services; // Removed IDispatcherService dependency
// using Newtonsoft.Json; // Use this if OpenFolderRequest needs Newtonsoft

namespace Illustra.Shared.Models.Tools
{
    // Tool Definition (from McpTools.cs)
    [McpTool(
        "open_folder",
        "Opens a specified folder in the Illustra application.",
        // language=json
        """
        {
          "type": "object",
          "properties": {
            "folderPath": { "type": "string", "description": "The absolute path of the folder to open." },
            "selectedFilePath": { "type": "string", "description": "Optional file path to select after opening the folder." }
          },
          "required": ["folderPath"]
        }
        """
    )]
    public class OpenFolderTool : IMcpToolDefinition, IToolExecutor // Implement IToolExecutor
    {
        // Removed IDispatcherService dependency

        // Constructor no longer needs IDispatcherService
        public OpenFolderTool()
        {
        }

        public async Task<object> ExecuteAsync(JObject arguments, IEventAggregator eventAggregator, ILogger logger, CancellationToken cancellationToken)
        {
            logger.LogInformation("Executing OpenFolderTool...");

            // Extract and validate arguments
            if (arguments == null || !arguments.TryGetValue("folderPath", StringComparison.OrdinalIgnoreCase, out var folderPathToken) || folderPathToken.Type != JTokenType.String)
            {
                logger.LogWarning("Missing or invalid 'folderPath' argument for 'open_folder' tool.");
                throw new ArgumentException("Missing or invalid 'folderPath' argument.");
            }
            string? folderPath = folderPathToken.Value<string>(); // CS8600 Fix: Allow null initially
            if (string.IsNullOrEmpty(folderPath))
            {
                logger.LogWarning("'folderPath' argument cannot be empty for 'open_folder' tool.");
                throw new ArgumentException("'folderPath' argument cannot be empty.");
            }

            // Optional: Extract selectedFilePath if present
            string? selectedFilePath = null; // CS8600 Fix: Explicitly declare as nullable
            if (arguments.TryGetValue("selectedFilePath", StringComparison.OrdinalIgnoreCase, out var selectedPathToken) && selectedPathToken.Type == JTokenType.String)
            {
                selectedFilePath = selectedPathToken.Value<string>();
            }


            // Prepare event args
            var tcs = new TaskCompletionSource<object>(); // TaskCompletionSource to await the result from the WPF side
            var args = new McpOpenFolderEventArgs
            {
                FolderPath = folderPath,
                SelectedFilePath = selectedFilePath, // Pass optional selected file path
                SourceId = "mcp-tool-execution", // Indicate source
                ResultCompletionSource = tcs
                // Removed CancellationToken = cancellationToken as it's not part of McpOpenFolderEventArgs
            };

            logger.LogInformation("Publishing McpOpenFolderEvent for path: {FolderPath}, SelectedFile: {SelectedFilePath}", folderPath, selectedFilePath ?? "None");

            // Publish the event directly. The subscriber (in WPF app) should handle UI thread dispatching if needed.
            eventAggregator.GetEvent<McpOpenFolderEvent>().Publish(args);

            // Wait for the result from the WPF side
            try
            {
                // Await the TaskCompletionSource. The WPF handler should call SetResult or SetException.
                var result = await tcs.Task;
                logger.LogInformation("OpenFolderTool execution completed via event. Result: {Result}", result);
                // Return the result obtained from the event handler
                // The structure of 'result' depends on what the WPF handler sets.
                // For open_folder, it might simply be a boolean indicating success.
                return result ?? new { success = false, message = "No result from handler." }; // Return a default failure object if null
            }
            catch (TaskCanceledException)
            {
                logger.LogWarning("OpenFolderTool execution was cancelled.");
                throw; // Re-throw cancellation
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error waiting for OpenFolderTool result from event handler.");
                throw; // Re-throw other exceptions
            }
        }
    }

    // Event and EventArgs (from MCPOpenFolderEvent.cs)
    public class McpOpenFolderEvent : PubSubEvent<McpOpenFolderEventArgs> { }

    public class McpOpenFolderEventArgs : McpBaseEventArgs
    {
        public string? FolderPath { get; set; } // CS8618 Fix: Allow null
        public string? SelectedFilePath { get; set; } // Optional file to select after opening
    }

    // Removed OpenFolderRequest class as arguments are handled directly as JObject in ExecuteAsync
}
