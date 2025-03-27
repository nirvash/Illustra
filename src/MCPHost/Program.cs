// This minimal Program.cs is sufficient when the host is configured and launched
// from App.xaml.cs using UseStartup<Startup>.
// If running MCPHost independently (e.g., via 'dotnet run'), this file would
// need to include the service registration and pipeline configuration
// similar to what was attempted before, or explicitly call UseStartup here.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// A minimal endpoint to confirm it's running if launched independently.
app.MapGet("/", () => "Illustra MCP Host (Minimal Endpoint)");

app.Run();
