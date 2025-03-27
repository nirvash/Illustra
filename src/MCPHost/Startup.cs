using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prism.Events; // Required for IEventAggregator if shared
using Illustra; // Required for App class reference

namespace Illustra.MCPHost
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Register controllers (needed for API endpoints)
            services.AddControllers();

            // Register a new instance of IEventAggregator
            services.AddSingleton<IEventAggregator, Prism.Events.EventAggregator>();

            // Register APIService as Singleton
            services.AddSingleton<APIService>();

            // Note: IEventAggregator is registered in App.xaml.cs's ConfigureServices
            // It's injected here if needed by services within this project.
            // Example: services.AddSingleton<MyServiceThatNeedsAggregator>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                // We will add Swagger UI later in Step 2
            }

            // Minimal routing setup for now
            app.UseRouting();

            // Enable controllers
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                // Basic root endpoint for testing if the server is running
                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("Illustra MCP Host is running.");
                });
            });
        }
    }
}
