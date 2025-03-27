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
        public async Task GetInfo_ValidRequest_CallsApiServiceAndReturnsOk()
        {
            // Arrange
            var toolName = "test-tool";
            var filePath = "test/path";
            var apiResponse = new { info = "test info" };

            _mockApiService
                .Setup(x => x.GetInfoAsync(
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

            _mockApiService.Verify(
                x => x.GetInfoAsync(
                    It.Is<string>(t => t == toolName),
                    It.Is<string>(p => p == filePath),
                    It.IsAny<CancellationToken>()),
                Times.Once);
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
    }
}
