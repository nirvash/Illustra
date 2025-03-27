using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Illustra.Shared.Models; // Updated namespace
using Illustra.Shared.Models; // For request/event args
using Microsoft.AspNetCore.Http; // For IHttpContextAccessor

namespace Illustra.MCPHost.Controllers
{
    /// <summary>
    /// Controller handling MCP requests.
    /// Defines endpoints for executing tools and potentially retrieving resources.
    /// </summary>
    [ApiController]
    [Route("api")] // Base route for all API endpoints
    public class McpController : ControllerBase
    {
        private readonly APIService _apiService;
        // Add ILogger if needed

        public McpController(APIService apiService, IHttpContextAccessor httpContextAccessor = null)
        {
            _apiService = apiService;
            HttpContext = httpContextAccessor?.HttpContext;
        }

        /// <summary>
        /// For unit testing purposes
        /// </summary>
        public Microsoft.AspNetCore.Http.HttpContext HttpContext { get; set; }

        // Placeholder for executing an operation tool
        // POST /api/execute/{tool_name}
        [HttpPost("execute/{toolName}")]
        public async Task<IActionResult> ExecuteTool(string toolName, [FromBody] ToolExecuteRequest request)
        {
            // TODO: Implement actual call to _apiService.ExecuteToolAsync(toolName, request.Parameters)
            // TODO: Add proper error handling (try-catch) and return appropriate status codes
            try
            {
                if (request == null)
                {
                    return BadRequest("Request body is required");
                }

                var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
                var result = await _apiService.ExecuteToolAsync(toolName, request.Parameters, cancellationToken);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error executing tool: {ex.Message}");
            }
        } // End of ExecuteTool method

        /// <summary>
        /// Opens a specified folder in the application.
        /// </summary>
        /// <param name="request">The request body containing the folder path.</param>
        /// <returns>Status indicating success or failure.</returns>
        /// <response code="200">If the folder was opened successfully.</response>
        /// <response code="400">If the request body or folder path is invalid.</response>
        /// <response code="500">If an internal server error occurs.</response>
        [HttpPost("commands/open_folder")] // Changed route to avoid conflict
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> OpenFolder([FromBody] OpenFolderRequest request) // Added public back for testing
        {
            try
            {
                if (request == null || string.IsNullOrEmpty(request.FolderPath))
                {
                    return BadRequest("Folder path is required.");
                }

                // Set a fixed SourceId indicating the request originated from the MCP Host API
                string sourceId = "MCPHost";

                bool success = await _apiService.OpenFolderAsync(request.FolderPath, sourceId);

                if (success)
                {
                    return Ok(new { Message = "Folder opened successfully." });
                }
                else
                {
                    // Consider returning a more specific error if APIService provides details
                    return StatusCode(500, "Failed to open folder.");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error opening folder: {ex.Message}");
            }
        }


        // Placeholder for retrieving information
        // GET /api/info/{tool_name}
        [HttpGet("info/{toolName}")]
        public async Task<IActionResult> GetInfo(string toolName, [FromQuery] string? filePath = null) // Example query param
        {
            // TODO: Implement actual call to _apiService.GetInfoAsync(toolName, queryParams...)
            // TODO: Add proper error handling (try-catch) and return appropriate status codes
            // Example (needs refinement based on APIService implementation):
            try
            {
                if (string.IsNullOrEmpty(toolName))
                {
                    return BadRequest("Tool name is required");
                }

                var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;

                // Check if the request is for the list of available tools
                if (toolName.Equals("available_tools", StringComparison.OrdinalIgnoreCase))
                {
                    var tools = await _apiService.GetAvailableToolsAsync(cancellationToken);
                    return Ok(tools);
                }
                else // Otherwise, get info for the specific tool
                {
                    var result = await _apiService.GetInfoAsync(toolName, filePath, cancellationToken);
                    return Ok(result ?? new { Message = $"No specific information available for tool '{toolName}'" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Error = "Internal server error",
                    Details = ex.Message,
                    ToolName = toolName
                });
            }
        }

        // GET /api/openapi.json (Handled by Swashbuckle middleware, no action needed here)
        // GET /api/openapi.yaml (Handled by Swashbuckle middleware, no action needed here)

    }
}
