using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Illustra.MCPHost.Models; // For InvokeRequest
using Illustra.Shared.Models; // For McpExecuteToolEventArgs etc. if needed later
using Microsoft.AspNetCore.Http;
using System.IO; // For StreamWriter, StreamReader
using System.Text; // For Encoding
using System; // For Exception
using System.Threading; // For CancellationToken
using Microsoft.Extensions.Logging; // Added for ILogger
using Newtonsoft.Json; // Added for JsonProperty attribute, JsonConvert
using System.Collections.Generic; // For List
using System.Linq; // For Linq extensions if needed

namespace Illustra.MCPHost.Controllers
{
    /// <summary>
    /// Controller handling MCP HTTP requests according to the specification.
    /// Defines endpoints for /start, /invoke, and /events.
    /// </summary>
    [ApiController]
    // [Route("api")] // Removed base route to match MCP spec (endpoints are at root)
    public class McpController : ControllerBase
    {
        private readonly APIService _apiService;
        private readonly ILogger<McpController> _logger; // Added logger

        // Simple in-memory store for SSE connections (replace with a robust solution if needed)
        // Consider using ConcurrentDictionary for thread safety if scaling
        private static readonly List<StreamWriter> _eventClients = new List<StreamWriter>();
        private static readonly object _lock = new object();

        public McpController(APIService apiService, ILogger<McpController> logger) // Added ILogger
        {
            _apiService = apiService;
            _logger = logger;
        }

        /// <summary>
        /// MCP Start endpoint. Client notification on startup.
        /// </summary>
        /// <returns>HTTP 200 OK</returns>
        [HttpPost("/start")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult Start()
        {
            _logger.LogInformation("Received /start request.");
            // No action needed, just acknowledge
            return Ok();
        }

        /// <summary>
        /// MCP Invoke endpoint. Executes a tool requested by the client.
        /// </summary>
        /// <param name="request">Request body containing tool_name and arguments.</param> // Parameter name kept for documentation
        /// <returns>HTTP 202 Accepted if the request is received, or an error status.</returns>
        [HttpPost("/invoke")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status200OK)] // Added for list_tools response
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        // public async Task<IActionResult> Invoke([FromBody] InvokeRequest request) // Keep [FromBody] commented out as manual reading is needed for tests
        public async Task<IActionResult> Invoke() // Keep parameter removed for manual reading
        {
            // Manually read and deserialize the request body as [FromBody] fails in TestServer with Newtonsoft.Json
            InvokeRequest request = null;
            string requestBody = string.Empty; // Store body for logging
            try
            {
                // Reading the body consumes it, so read it once here.
                using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true); // leaveOpen might be important if other middleware needs the body
                requestBody = await reader.ReadToEndAsync();
                _logger.LogDebug("Manually read /invoke body: {Body}", requestBody);

                if (!string.IsNullOrEmpty(requestBody))
                {
                    request = JsonConvert.DeserializeObject<InvokeRequest>(requestBody);
                }
                // If body is empty, request remains null, handled by validation below.
            }
            catch (JsonException jsonEx) // Catch specific JSON errors
            {
                _logger.LogWarning(jsonEx, "Error deserializing request body JSON: {Body}", requestBody);
                // Return BadRequest for malformed JSON
                return BadRequest("Invalid JSON format in request body.");
            }
            catch (Exception ex) // Catch other potential errors during reading
            {
                _logger.LogError(ex, "Error manually reading request body.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error processing request body.");
            }

            // The previously added debug log block (lines 65-77) is now removed as it's redundant.

            _logger.LogInformation("Processing /invoke request for tool: {ToolName}", request?.ToolName); // Changed log message slightly
            if (request == null || string.IsNullOrEmpty(request.ToolName))
            {
                _logger.LogWarning("/invoke request received with missing tool_name.");
                return BadRequest("Request body must include 'tool_name'.");
            }

            var cancellationToken = HttpContext.RequestAborted;

            // Check if it's the built-in list_tools command
            if (request.ToolName.Equals("list_tools", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _logger.LogInformation("Handling 'list_tools' request.");
                    var tools = await _apiService.GetAvailableToolsAsync(cancellationToken);
                    // Return the list directly in the response body with OK status
                    return Ok(tools);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling 'list_tools' request.");
                    return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving tool list.");
                }
            }
            else // Handle other tools via APIService.InvokeToolAsync
            {
                try
                {
                    // Execute fire-and-forget via APIService.InvokeToolAsync
                    _ = Task.Run(async () => {
                        try
                        {
                            await _apiService.InvokeToolAsync(request.ToolName, request.Arguments, cancellationToken);
                            // Success/error logging and potential SSE events are handled within InvokeToolAsync/OpenFolderAsync
                        }
                        catch (ArgumentException argEx) // Catch specific argument errors from InvokeToolAsync
                        {
                            _logger.LogWarning("Argument error during background tool invocation: {ErrorMessage}", argEx.Message);
                            // TODO: Send SSE error event if required by spec
                            // await SendEventToClientsAsync("tool_error", new { tool = request.ToolName, message = $"Argument error: {argEx.Message}" });
                        }
                        catch (NotSupportedException nsEx) // Catch unsupported tool errors
                        {
                            _logger.LogWarning("Unsupported tool error during background tool invocation: {ErrorMessage}", nsEx.Message);
                            // TODO: Send SSE error event if required by spec
                            // await SendEventToClientsAsync("tool_error", new { tool = request.ToolName, message = nsEx.Message });
                        }
                        catch (Exception ex) // Catch general errors during background execution
                        {
                            _logger.LogError(ex, "Error during background execution of tool '{ToolName}'.", request.ToolName);
                            // TODO: Send SSE error event if required by spec
                            // await SendEventToClientsAsync("tool_error", new { tool = request.ToolName, message = $"Internal error: {ex.Message}" });
                        }
                    }, cancellationToken); // Pass cancellation token

                    // Return 202 Accepted immediately
                    return Accepted();
                }
                catch (Exception ex) // Catch potential errors during the *initiation* of the background task
                {
                    _logger.LogError(ex, "Error initiating background execution for tool '{ToolName}'.", request.ToolName);
                    // This is less likely but possible if Task.Run itself fails.
                    return StatusCode(StatusCodes.Status500InternalServerError, $"Error initiating execution for tool '{request.ToolName}'.");
                }
            }
        }
        // Removed ExecuteAndSendResultAsync helper method as it depended on the removed ExecuteToolAsync

        /// <summary>
        /// MCP Events endpoint. Provides real-time responses using Server-Sent Events (SSE).
        /// </summary>
        /// <returns>An SSE stream (text/event-stream).</returns>
        [HttpGet("/events")]
        public async Task GetEvents(CancellationToken cancellationToken) // Added CancellationToken
        {
            _logger.LogInformation("Client connected to /events SSE stream.");
            Response.Headers.Append("Content-Type", "text/event-stream");
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");

            StreamWriter clientWriter = null;
            try
            {
                // Use Response.Body directly for async writing
                clientWriter = new StreamWriter(Response.Body, Encoding.UTF8, 1024, leaveOpen: true); // leaveOpen: true is important
                await clientWriter.FlushAsync(cancellationToken); // Ensure headers are sent

                lock (_lock)
                {
                    _eventClients.Add(clientWriter);
                }
                _logger.LogInformation("SSE client added. Total clients: {ClientCount}", _eventClients.Count);

                // Send a confirmation event
                _logger.LogInformation("Attempting to send 'server_ready' event...");
                await SendSseMessageAsync(clientWriter, "server_ready", new { message = "Connected to Illustra MCP Host event stream." }, cancellationToken);
                _logger.LogInformation("'server_ready' event sent (or attempted).");


                // Keep the connection open until the client disconnects or cancellation is requested
                // Use Task.Delay with an infinite timeout, which respects the CancellationToken
                await Task.Delay(Timeout.Infinite, cancellationToken);

            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("SSE client disconnected (OperationCanceledException).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in /events SSE stream.");
                // Don't re-throw, as the connection might be broken anyway.
            }
            finally
            {
                if (clientWriter != null)
                {
                    lock (_lock)
                    {
                        _eventClients.Remove(clientWriter);
                    }
                    _logger.LogInformation("SSE client removed. Total clients: {ClientCount}", _eventClients.Count);
                    try
                    {
                        // Dispose the writer, which should close the underlying stream if not left open
                        await clientWriter.DisposeAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Exception disposing StreamWriter for SSE client.");
                    }
                }
                _logger.LogInformation("SSE connection closed.");
            }
        }

        // Helper method to send an event to all connected SSE clients
        private async Task SendEventToClientsAsync(string eventName, object data)
        {
            List<StreamWriter> clientsToRemove = new List<StreamWriter>();
            List<StreamWriter> currentClients;

            lock (_lock)
            {
                currentClients = new List<StreamWriter>(_eventClients); // Copy the list to iterate safely
            }
            _logger.LogInformation("Sending event '{EventName}' to {ClientCount} clients.", eventName, currentClients.Count);


            foreach (var client in currentClients)
            {
                try
                {
                    // Use a CancellationToken that triggers quickly if writing fails
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await SendSseMessageAsync(client, eventName, data, cts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send event '{EventName}' to an SSE client. Marking for removal.", eventName);
                    clientsToRemove.Add(client);
                }
            }

            if (clientsToRemove.Count > 0)
            {
                lock (_lock)
                {
                    foreach (var clientToRemove in clientsToRemove)
                    {
                        _eventClients.Remove(clientToRemove);
                        // Attempt to dispose the writer outside the loop if needed, but removal is primary
                        try { clientToRemove.Dispose(); } catch { /* Ignore disposal errors */ }
                    }
                }
                _logger.LogInformation("Removed {RemovedCount} disconnected SSE clients.", clientsToRemove.Count);
            }
        }

        // Helper method to format and send an SSE message
        private async Task SendSseMessageAsync(StreamWriter writer, string eventName, object data, CancellationToken cancellationToken)
        {
            try // Added try-catch for robustness
            {
                string jsonData = Newtonsoft.Json.JsonConvert.SerializeObject(data ?? new object()); // Ensure data is not null

                // Format according to SSE specification (multi-line data needs multiple 'data:' lines)
                var lines = jsonData.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                await writer.WriteAsync($"event: {eventName}\n".AsMemory(), cancellationToken);
                foreach (var line in lines)
                {
                    await writer.WriteAsync($"data: {line}\n".AsMemory(), cancellationToken);
                }
                await writer.WriteAsync("\n".AsMemory(), cancellationToken); // End of message
                await writer.FlushAsync(cancellationToken); // Ensure data is sent immediately
                _logger.LogDebug("Sent SSE event: {EventName}, Data: {JsonData}", eventName, jsonData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during SendSseMessageAsync for event {EventName}.", eventName);
                // Re-throwing might disconnect the client, consider just logging
                // throw;
            }
        }


    } // End of McpController class
} // End of namespace
