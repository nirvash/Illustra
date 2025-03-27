using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Illustra.Shared.Models; // Updated namespace
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
                var result = await _apiService.GetInfoAsync(toolName, filePath, cancellationToken);

                return Ok(result ?? new { Message = "No information available" });
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
