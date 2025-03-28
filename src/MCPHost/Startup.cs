using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prism.Events;
using System.Linq;
using Illustra.Shared.Models.Tools; // Added for tool registration
using System; // For IServiceProvider

namespace Illustra.MCPHost
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // Add using Illustra.Services; at the top if not present
        public void ConfigureServices(IServiceCollection services)
        {
            System.Diagnostics.Debug.WriteLine("Configuring services...");

            services.AddControllers()
                .AddApplicationPart(typeof(Controllers.McpController).Assembly)
                .AddNewtonsoftJson(); // Use Newtonsoft.Json for model binding

            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "Illustra MCP API",
                    Version = "v1",
                    Description = "The API for Illustra Multi-Command Protocol"
                });
            });

            // Register tool executors (assuming transient lifetime is suitable)
            services.AddTransient<OpenFolderTool>();
            // Add other tool executor registrations here

            // IEventAggregator is registered in App.xaml.cs
            // APIService now depends on IServiceProvider as well
            services.AddScoped<APIService>(); // Register APIService

            // List registered controllers
            var controllerTypes = typeof(Controllers.McpController).Assembly
                .GetTypes()
                .Where(t => t.Name.EndsWith("Controller"))
                .ToList();

            foreach (var controller in controllerTypes)
            {
                System.Diagnostics.Debug.WriteLine($"Found controller: {controller.FullName}");
            }
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            System.Diagnostics.Debug.WriteLine("Configuring application...");

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSwagger(options =>
            {
                options.RouteTemplate = "api/{documentName}/openapi.json";
            });

            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/api/v1/openapi.json", "Illustra MCP API v1");
                options.RoutePrefix = "api/swagger";
            });

            // Log all requests with more details
            app.Use(async (context, next) =>
            {
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                var request = context.Request;
                var response = context.Response;

                // Log incoming request details
                var headers = string.Join(", ", request.Headers.Select(h => $"{h.Key}={h.Value}")); // Format headers
                System.Diagnostics.Debug.WriteLine($"--> {request.Method} {request.Path}{request.QueryString} | Headers: [{headers}]");

                await next(); // Call the next middleware

                var elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;

                // Log outgoing response details
                System.Diagnostics.Debug.WriteLine($"<-- {request.Method} {request.Path}{request.QueryString} - {response.StatusCode} in {elapsedMs:0.000} ms");
            });

            app.UseRouting();

            // Log endpoint selection
            app.Use(async (context, next) =>
            {
                var endpoint = context.GetEndpoint();
                System.Diagnostics.Debug.WriteLine($"Selected endpoint: {endpoint?.DisplayName ?? "None"}");
                if (endpoint == null)
                {
                    System.Diagnostics.Debug.WriteLine("Available endpoints:");
                    var endpointDataSource = app.ApplicationServices.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>().FirstOrDefault();
                    if (endpointDataSource != null)
                    {
                        foreach (var ep in endpointDataSource.Endpoints)
                        {
                            System.Diagnostics.Debug.WriteLine($"- {ep.DisplayName}");
                        }
                    }
                }
                await next();
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                // Redirect root path "/" to "/events"
                endpoints.MapGet("/", context =>
                {
                    context.Response.Redirect("/events", permanent: false); // Use temporary redirect
                    return Task.CompletedTask;
                });
            });

            System.Diagnostics.Debug.WriteLine("Application configuration completed.");
        }
    }
}
