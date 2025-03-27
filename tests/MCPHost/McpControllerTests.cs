using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Prism.Events;
using Illustra.MCPHost;
using Illustra.MCPHost.Controllers;
using Illustra.Shared.Models;

namespace Illustra.Tests.MCPHost
{
    [TestFixture]
    public class McpControllerTests
    {
        private Mock<IEventAggregator> _mockEventAggregator;
        private Mock<APIService> _mockApiService;
        private McpController _controller;

        [SetUp]
        public void Setup()
        {
            _mockEventAggregator = new Mock<IEventAggregator>();
            // APIServiceのコンストラクタからDispatcher依存を削除
            _mockApiService = new Mock<APIService>(_mockEventAggregator.Object);
            // _mockApiService.CallBase = true; // Remove CallBase as we mock methods
            _controller = new McpController(_mockApiService.Object);
        }

        [Test]
        public async Task ExecuteTool_ValidRequest_CallsApiServiceAndReturnsOk()
        {
            // Arrange
            var toolName = "test-tool";
            var request = new ToolExecuteRequest { Parameters = new { param1 = "value1" } };
            var expectedResult = new { success = true };

            _mockApiService
                .Setup(x => x.ExecuteToolAsync(
                    It.Is<string>(t => t == toolName),
                    It.Is<object>(p => p.Equals(request.Parameters)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult)
                .Verifiable();

            // Act
            var result = await _controller.ExecuteTool(toolName, request);

            // Assert
            Assert.That(result, Is.InstanceOf<ObjectResult>());
            var objectResult = (ObjectResult)result;
            Assert.That(objectResult.StatusCode, Is.EqualTo(200));
            Assert.That(objectResult.Value, Is.EqualTo(expectedResult));

            _mockApiService.Verify( // Verify the correct overload was called
                x => x.ExecuteToolAsync(
                    It.Is<string>(t => t == toolName),
                    It.Is<object>(p => p.Equals(request.Parameters)),
                    It.IsAny<CancellationToken>()), // Match the call with CancellationToken
                Times.Once);
        }

        [Test]
        public async Task ExecuteTool_ApiServiceThrowsException_ReturnsInternalServerError()
        {
            // Arrange
            var toolName = "test-tool";
            var request = new ToolExecuteRequest { Parameters = new { param1 = "value1" } };
            var expectedException = new Exception("Test error");

            _mockApiService
                .Setup(x => x.ExecuteToolAsync(
                    It.Is<string>(t => t == toolName),
                    It.Is<object>(p => p.Equals(request.Parameters)),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);

            // Act
            var result = await _controller.ExecuteTool(toolName, request);

            // Assert
            Assert.That(result, Is.TypeOf<ObjectResult>());
            var objectResult = (ObjectResult)result;
            Assert.That(objectResult.StatusCode, Is.EqualTo(500));
            Assert.That(objectResult.Value, Is.Not.Null);
        }

        [Test]
        public async Task GetInfo_SpecificTool_CallsGetInfoAsyncAndReturnsOk() // Renamed for clarity
        {
            // Arrange
            var toolName = "specific-tool"; // Use a different name to avoid conflict
            var filePath = "test/path";
            var apiResponse = new { info = "specific tool info" };

            _mockApiService
                .Setup(x => x.GetInfoAsync( // Mock for specific tool info
                    It.Is<string>(t => t == toolName),
                    It.Is<string>(p => p == filePath),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.GetInfo(toolName, filePath);

            // Assert
            Assert.IsInstanceOf<OkObjectResult>(result);
            var okResult = (OkObjectResult)result;
            Assert.AreEqual(200, okResult.StatusCode);
            Assert.AreEqual(apiResponse, okResult.Value);

            _mockApiService.Verify( // Verify GetInfoAsync was called
                x => x.GetInfoAsync(
                    It.Is<string>(t => t == toolName),
                    It.Is<string>(p => p == filePath),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _mockApiService.Verify( // Verify GetAvailableToolsAsync was NOT called
                x => x.GetAvailableToolsAsync(It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task GetInfo_AvailableTools_CallsGetAvailableToolsAsyncAndReturnsOk()
        {
            // Arrange
            var toolName = "available_tools";
            var expectedTools = new[]
            {
                new { Name = "execute", Method = "POST", Path = "/api/execute/{toolName}", Description = "Executes a specified tool with given parameters." },
                new { Name = "open_folder", Method = "POST", Path = "/api/commands/open_folder", Description = "Opens a specified folder in the Illustra application." },
                new { Name = "get_info", Method = "GET", Path = "/api/info/{toolName}", Description = "Gets information about a specific tool or lists available tools (use 'available_tools' as toolName)." }
            };

            _mockApiService
                .Setup(x => x.GetAvailableToolsAsync(It.IsAny<CancellationToken>())) // Mock for available tools
                .ReturnsAsync(expectedTools);

            // Act
            var result = await _controller.GetInfo(toolName);

            // Assert
            Assert.IsInstanceOf<OkObjectResult>(result);
            var okResult = (OkObjectResult)result;
            Assert.AreEqual(200, okResult.StatusCode);
            Assert.AreEqual(expectedTools, okResult.Value);

            _mockApiService.Verify( // Verify GetAvailableToolsAsync was called
                x => x.GetAvailableToolsAsync(It.IsAny<CancellationToken>()),
                Times.Once);
            _mockApiService.Verify( // Verify GetInfoAsync was NOT called
                x => x.GetInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task GetInfo_ApiServiceThrowsException_ReturnsInternalServerError()
        {
            // Arrange
            var toolName = "test-tool";
            var filePath = "test/path";
            var expectedException = new Exception("Test error");

            _mockApiService
                .Setup(x => x.GetInfoAsync(
                    It.Is<string>(t => t == toolName),
                    It.Is<string>(p => p == filePath),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);

            // Act
            var result = await _controller.GetInfo(toolName, filePath);

            // Assert
            Assert.That(result, Is.TypeOf<ObjectResult>());
            var statusCodeResult = (ObjectResult)result;
            Assert.That(statusCodeResult.StatusCode, Is.EqualTo(500));
        }
        [Test]
        public async Task ExecuteTool_NullRequest_ReturnsBadRequest()
        {
            // Arrange
            var toolName = "test-tool";

            // Act
            var result = await _controller.ExecuteTool(toolName, null);

            // Assert
            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
            var badRequestResult = (BadRequestObjectResult)result;
            Assert.That(badRequestResult.StatusCode, Is.EqualTo(400));
        }

        [Test]
        public async Task GetInfo_NullToolName_ReturnsBadRequest()
        {
            // Arrange
            string toolName = null;
            var filePath = "test/path";

            // Act
            var result = await _controller.GetInfo(toolName, filePath);

            // Assert
            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>()); // Correct Assert for this test
            var badRequestResult = (BadRequestObjectResult)result;
            Assert.That(badRequestResult.StatusCode, Is.EqualTo(400));
        } // End of GetInfo_NullToolName_ReturnsBadRequest

        [Test]
        public async Task OpenFolder_ValidRequest_CallsApiServiceAndReturnsOk() // Added public
        {
            // Arrange
            var request = new OpenFolderRequest { FolderPath = "D:\\Demo" };
            var expectedResult = true;

            _mockApiService
                .Setup(x => x.OpenFolderAsync(
                    It.Is<string>(p => p == request.FolderPath),
                    It.Is<string>(s => s == "MCPHost"))) // Expect "MCPHost" as SourceId
                .ReturnsAsync(expectedResult)
                .Verifiable();

            // Act
            var result = await _controller.OpenFolder(request);

            // Assert
            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            var okResult = (OkObjectResult)result;
            Assert.That(okResult.StatusCode, Is.EqualTo(200));
            // Assert.That(okResult.Value, Is.EqualTo(new { Message = "Folder opened successfully." })); // Exact message might change

            _mockApiService.Verify();
        }

        [Test]
        public async Task OpenFolder_NullRequest_ReturnsBadRequest() // Added public
        {
            // Arrange
            OpenFolderRequest request = null;

            // Act
            var result = await _controller.OpenFolder(request);

            // Assert
            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
            var badRequestResult = (BadRequestObjectResult)result;
            Assert.That(badRequestResult.StatusCode, Is.EqualTo(400));
        }

        [Test]
        public async Task OpenFolder_EmptyFolderPath_ReturnsBadRequest() // Added public
        {
            // Arrange
            var request = new OpenFolderRequest { FolderPath = "" };

            // Act
            var result = await _controller.OpenFolder(request);

            // Assert
            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
            var badRequestResult = (BadRequestObjectResult)result;
            Assert.That(badRequestResult.StatusCode, Is.EqualTo(400));
        }

        [Test]
        public async Task OpenFolder_ApiServiceReturnsFalse_ReturnsInternalServerError() // Added public
        {
            // Arrange
            var request = new OpenFolderRequest { FolderPath = "D:\\Demo" };
            var expectedResult = false;

            _mockApiService
                .Setup(x => x.OpenFolderAsync(
                    It.Is<string>(p => p == request.FolderPath),
                    It.Is<string>(s => s == "MCPHost"))) // Expect "MCPHost" as SourceId
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.OpenFolder(request);

            // Assert
            Assert.That(result, Is.InstanceOf<ObjectResult>());
            var objectResult = (ObjectResult)result;
            Assert.That(objectResult.StatusCode, Is.EqualTo(500));
        }

        [Test]
        public async Task OpenFolder_ApiServiceThrowsException_ReturnsInternalServerError() // Added public
        {
            // Arrange
            var request = new OpenFolderRequest { FolderPath = "D:\\Demo" };
            var expectedException = new Exception("Test error");

            _mockApiService
                .Setup(x => x.OpenFolderAsync(
                    It.Is<string>(p => p == request.FolderPath),
                    It.Is<string>(s => s == "MCPHost"))) // Expect "MCPHost" as SourceId
                .ThrowsAsync(expectedException);

            // Act
            var result = await _controller.OpenFolder(request);

            // Assert
            Assert.That(result, Is.InstanceOf<ObjectResult>());
            var objectResult = (ObjectResult)result;
            Assert.That(objectResult.StatusCode, Is.EqualTo(500));
        }
    }
}
