using System.Threading;
using System.Threading.Tasks;
using Prism.Events;
using Illustra.Shared.Models; // For McpBaseEventArgs
using Illustra.Shared.Models.Tools; // Added for McpOpenFolderEvent, McpOpenFolderEventArgs, IToolExecutor
using Newtonsoft.Json.Linq; // Added for JObject
using Microsoft.Extensions.Logging; // Added for ILogger
using System; // For IServiceProvider, NotSupportedException, ArgumentException
using System.Reflection; // For Assembly
using System.Linq; // For Linq extensions
using Microsoft.Extensions.DependencyInjection; // For IServiceProvider, GetRequiredService
using Illustra.Shared.Attributes; // For McpToolAttribute


namespace Illustra.MCPHost
{
    /// <summary>
    /// Handles communication between the Web API and the WPF application logic.
    /// Uses IEventAggregator to publish events that WPF ViewModels/Services subscribe to.
    /// Discovers and executes tools defined in the Shared assembly.
    /// </summary>
    // using Illustra.Services; is no longer needed
    public class APIService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly ILogger<APIService> _logger; // Added logger
        private readonly IServiceProvider _serviceProvider; // To resolve tool instances

        public APIService(IEventAggregator eventAggregator, ILogger<APIService> logger, IServiceProvider serviceProvider) // Added IServiceProvider injection
        {
            _eventAggregator = eventAggregator;
            _logger = logger; // Assign logger
            _serviceProvider = serviceProvider; // Assign service provider
        }

        /// <summary>
        /// Invokes the specified tool with the given arguments by finding and executing the corresponding IToolExecutor.
        /// </summary>
        /// <param name="toolName">The name of the tool to invoke.</param>
        /// <param name="arguments">The arguments for the tool.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation, returning the result from the tool executor.</returns>
        public virtual async Task<object> InvokeToolAsync(string toolName, JObject arguments, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Attempting to invoke tool: {ToolName}", toolName);

            // Find the tool executor type based on the McpToolAttribute name
            var executorType = FindToolExecutorType(toolName);

            if (executorType == null)
            {
                _logger.LogWarning("Tool executor not found for tool: {ToolName}", toolName);
                throw new NotSupportedException($"Tool '{toolName}' is not supported or not found.");
            }

            // Resolve the tool executor instance from the service provider
            IToolExecutor executorInstance;
            try
            {
                // Use GetRequiredService to ensure the tool is registered in DI
                executorInstance = (IToolExecutor)_serviceProvider.GetRequiredService(executorType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve tool executor for type: {ExecutorType}", executorType.FullName);
                throw new InvalidOperationException($"Could not resolve executor for tool '{toolName}'. Ensure it's registered in DI.", ex);
            }


            _logger.LogInformation("Found executor {ExecutorType} for tool {ToolName}. Executing...", executorType.Name, toolName);
            try
            {
                // Execute the tool
                // Pass dependencies (eventAggregator, logger) to the executor method
                // Note: We pass ILogger<APIService> cast to ILogger. Consider injecting ILoggerFactory if specific logger categories are needed per tool.
                return await executorInstance.ExecuteAsync(arguments, _eventAggregator, _logger, cancellationToken);
            }
            catch (ArgumentException argEx) // Catch argument validation errors from the tool itself
            {
                 _logger.LogWarning(argEx, "Argument error during execution of tool '{ToolName}'.", toolName);
                 throw; // Re-throw argument exceptions to be potentially handled as BadRequest by the controller
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during execution of tool '{ToolName}'.", toolName);
                // TODO: Send SSE error event if required by spec
                throw; // Re-throw other exceptions to be potentially handled as 500 by the controller
            }
        }

        private Type? FindToolExecutorType(string toolName) // CS8603 Fix: Return type can be null
        {
            // Search in the assembly containing the tool definitions (Illustra.Shared)
            // Assumes tool classes implement IToolExecutor and have McpToolAttribute
            var toolTypes = typeof(Illustra.Shared.Models.Tools.IToolExecutor).Assembly.GetTypes()
                .Where(t => typeof(Illustra.Shared.Models.Tools.IToolExecutor).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in toolTypes)
            {
                var attr = type.GetCustomAttribute<Illustra.Shared.Attributes.McpToolAttribute>();
                if (attr != null && attr.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase))
                {
                    return type;
                }
            }
            return null; // Not found
        }


        // Removed ExecuteToolAsync methods (2 overloads)
        // Removed GetInfoAsync methods (3 overloads)
        // Removed OpenFolderAsync method as its logic is now within OpenFolderTool.ExecuteAsync

        /// <summary>
        /// 利用可能なツール（APIエンドポイント）のリストを取得します。
        /// </summary>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>利用可能なツールの定義リストを含むオブジェクト</returns>
        public virtual Task<object> GetAvailableToolsAsync(CancellationToken cancellationToken)
        {
            // Use reflection to find tool definitions in the Shared assembly
            var toolDefinitions = typeof(Illustra.Shared.Models.IMcpToolDefinition).Assembly.GetTypes()
                .Where(t => typeof(Illustra.Shared.Models.IMcpToolDefinition).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .Select(t => t.GetCustomAttributes(typeof(Illustra.Shared.Attributes.McpToolAttribute), false).FirstOrDefault())
                .OfType<Illustra.Shared.Attributes.McpToolAttribute>() // Filter out nulls and cast
                .Select(attr =>
                {
                    try
                    {
                        // Parse the JSON schema string into an object
                        var schemaObject = Newtonsoft.Json.JsonConvert.DeserializeObject(attr.InputSchemaJson);
                        return new
                        {
                            name = attr.Name,
                            description = attr.Description,
                            inputSchema = schemaObject
                        };
                    }
                    catch (Exception ex)
                    {
                        // Log error parsing schema? For now, return null or a placeholder
                        System.Diagnostics.Debug.WriteLine($"Error parsing schema for tool '{attr.Name}': {ex.Message}");
                        return null;
                    }
                })
                .Where(t => t != null) // Filter out tools with invalid schemas
                .ToList();

            // MCP 仕様に合わせた形式で返す { "tools": [...] }
            return Task.FromResult<object>(new { tools = toolDefinitions });
        }
    }
}
