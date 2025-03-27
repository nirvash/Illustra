using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System.Net;
using System.Threading.Tasks;
using Illustra.MCPHost;

namespace Illustra.Tests.MCPHost
{
    [TestFixture]
    public class SwaggerTests
    {
        private IHost _host;
        private TestServer _server;

        [OneTimeSetUp]
        public void Setup()
        {
            var builder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.UseStartup<Startup>();
                });

            _host = builder.Start();
            _server = _host.GetTestServer();
        }

        [OneTimeTearDown]
        public async Task TearDown()
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        [Test]
        public async Task SwaggerUI_EndpointExists_ReturnsOk()
        {
            // Act
            var client = _server.CreateClient();
            client.DefaultRequestHeaders.Add("Accept", "text/html");

            // Make request to Swagger UI endpoint
            var response = await client.GetAsync("/api/swagger/index.html"); // Use direct path to avoid redirects

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task SwaggerJson_EndpointExists_ReturnsOk()
        {
            // Act
            var response = await _server.CreateClient().GetAsync("/api/v1/openapi.json");

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var content = await response.Content.ReadAsStringAsync();
            var json = System.Text.Json.JsonDocument.Parse(content);
            Assert.Multiple(() =>
            {
                // API Information
                Assert.That(json.RootElement.GetProperty("info").GetProperty("title").GetString(), Is.EqualTo("Illustra MCP API"));
                Assert.That(json.RootElement.GetProperty("info").GetProperty("version").GetString(), Is.EqualTo("v1"));

                // Endpoints
                var paths = json.RootElement.GetProperty("paths");
                Assert.That(paths.GetProperty("/api/execute/{toolName}").GetProperty("post").GetProperty("tags")[0].GetString(), Is.EqualTo("Mcp"));
                Assert.That(paths.GetProperty("/api/commands/open_folder").GetProperty("post").GetProperty("responses").GetProperty("400").GetProperty("description").GetString(), Is.EqualTo("Bad Request"));
                Assert.That(paths.GetProperty("/api/info/{toolName}").GetProperty("get").GetProperty("parameters")[0].GetProperty("required").GetBoolean(), Is.True);

                // Schemas
                var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
                Assert.That(schemas.GetProperty("OpenFolderRequest").GetProperty("properties").GetProperty("folderPath").GetProperty("type").GetString(), Is.EqualTo("string"));
                Assert.That(schemas.GetProperty("ToolExecuteRequest").GetProperty("properties").GetProperty("parameters").GetProperty("nullable").GetBoolean(), Is.True);
            });
        }
    }
}
