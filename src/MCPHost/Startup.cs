using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prism.Events;
// using Illustra.Services; // Removed - No longer needed
using System.Linq;

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
                .AddApplicationPart(typeof(Controllers.McpController).Assembly);

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

            // IEventAggregator and IDispatcherService are registered in App.xaml.cs
            services.AddScoped<APIService>(); // APIService depends on them

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

            // Log all requests
            app.Use(async (context, next) =>
            {
                System.Diagnostics.Debug.WriteLine($"Incoming request: {context.Request.Method} {context.Request.Path}");
                await next();
                System.Diagnostics.Debug.WriteLine($"Response status: {context.Response.StatusCode}");
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
                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("Illustra MCP Host is running.");
                });
            });

            System.Diagnostics.Debug.WriteLine("Application configuration completed.");
        }
    }
}
