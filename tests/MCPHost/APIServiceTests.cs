using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Prism.Events;
using Illustra.MCPHost;
using Illustra.Shared.Models; // Updated namespace
using Illustra.Services; // Added for IDispatcherService

namespace Illustra.Tests.MCPHost
{
    [TestFixture]
    public class APIServiceTests
    {
        private Mock<IEventAggregator> _mockEventAggregator;
        private Mock<McpExecuteToolEvent> _mockExecuteToolEvent; // Renamed
        private Mock<McpGetInfoEvent> _mockGetInfoEvent; // Renamed
        private APIService _apiService;

        [SetUp]
        public void Setup()
        {
            _mockEventAggregator = new Mock<IEventAggregator>();
            _mockExecuteToolEvent = new Mock<McpExecuteToolEvent>(); // Renamed
            _mockGetInfoEvent = new Mock<McpGetInfoEvent>(); // Renamed

            _mockEventAggregator
                .Setup(x => x.GetEvent<McpExecuteToolEvent>()) // Renamed
                .Returns(_mockExecuteToolEvent.Object);

            _mockEventAggregator
                .Setup(x => x.GetEvent<McpGetInfoEvent>()) // Renamed
                .Returns(_mockGetInfoEvent.Object);

            // Add mock for IDispatcherService
            var mockDispatcherService = new Mock<IDispatcherService>();
            // Setup InvokeAsync to execute the action immediately for testing
            // For InvokeAsync that returns Task, setup needs to return Task.CompletedTask
            // mockDispatcherService.Setup(d => d.InvokeAsync(It.IsAny<Action>())) // No longer needed
            //                      .Callback<Action>(action => action())
            //                      .Returns(Task.CompletedTask);
            _apiService = new APIService(_mockEventAggregator.Object); // Removed dispatcher service mock
        }

        [Test]
        public async Task ExecuteToolAsync_ValidCall_PublishesCorrectEvent()
        {
            // Arrange
            var toolName = "test-tool";
            var parameters = new { param1 = "value1" };
            McpExecuteToolEventArgs capturedArgs = null; // Renamed

            _mockExecuteToolEvent
                .Setup(x => x.Publish(It.IsAny<McpExecuteToolEventArgs>())) // Renamed
                .Callback<McpExecuteToolEventArgs>(args => capturedArgs = args); // Renamed

            // Act
            var task = _apiService.ExecuteToolAsync(toolName, parameters);

            // Assert
            Assert.That(capturedArgs, Is.Not.Null);
            Assert.That(capturedArgs.ToolName, Is.EqualTo(toolName));
            Assert.That(capturedArgs.Parameters, Is.EqualTo(parameters));
            Assert.That(capturedArgs.ResultCompletionSource, Is.Not.Null);
            Assert.That(capturedArgs.CancellationToken, Is.EqualTo(CancellationToken.None));

            // Complete the task to avoid test hanging
            capturedArgs.ResultCompletionSource.SetResult("test-result");
            await task;
        }

        [Test]
        public async Task ExecuteToolAsync_SubscriberSetsResult_ReturnsResult()
        {
            // Arrange
            var expectedResult = "test-result";
            McpExecuteToolEventArgs capturedArgs = null; // Renamed

            _mockExecuteToolEvent
                .Setup(x => x.Publish(It.IsAny<McpExecuteToolEventArgs>())) // Renamed
                .Callback<McpExecuteToolEventArgs>(args => capturedArgs = args); // Renamed

            // Act
            var task = _apiService.ExecuteToolAsync("test-tool", new { });
            capturedArgs.ResultCompletionSource.SetResult(expectedResult);
            var result = await task;

            // Assert
            Assert.That(result, Is.EqualTo(expectedResult));
        }

        [Test]
        public void ExecuteToolAsync_SubscriberSetsException_ThrowsException()
        {
            // Arrange
            var expectedException = new Exception("Test error");
            McpExecuteToolEventArgs capturedArgs = null; // Renamed

            _mockExecuteToolEvent
                .Setup(x => x.Publish(It.IsAny<McpExecuteToolEventArgs>())) // Renamed
                .Callback<McpExecuteToolEventArgs>(args => capturedArgs = args); // Renamed

            // Act & Assert
            var task = _apiService.ExecuteToolAsync("test-tool", new { });
            capturedArgs.ResultCompletionSource.SetException(expectedException);
            Assert.ThrowsAsync<Exception>(() => task);
        }

        [Test]
        public async Task GetInfoAsync_ValidCall_PublishesCorrectEvent()
        {
            // Arrange
            var toolName = "test-tool";
            var filePath = "test/path";
            McpGetInfoEventArgs capturedArgs = null; // Renamed

            _mockGetInfoEvent
                .Setup(x => x.Publish(It.IsAny<McpGetInfoEventArgs>())) // Renamed
                .Callback<McpGetInfoEventArgs>(args => capturedArgs = args); // Renamed

            // Act
            var task = _apiService.GetInfoAsync(toolName, filePath);

            // Assert
            Assert.That(capturedArgs, Is.Not.Null);
            Assert.That(capturedArgs.ToolName, Is.EqualTo(toolName));
            Assert.That(capturedArgs.FilePath, Is.EqualTo(filePath));
            Assert.That(capturedArgs.ResultCompletionSource, Is.Not.Null);
            Assert.That(capturedArgs.CancellationToken, Is.EqualTo(CancellationToken.None));

            // Complete the task to avoid test hanging
            capturedArgs.ResultCompletionSource.SetResult("test-info");
            await task;
        }

        [Test]
        public async Task GetInfoAsync_SubscriberSetsResult_ReturnsResult()
        {
            // Arrange
            var expectedResult = "test-info";
            McpGetInfoEventArgs capturedArgs = null; // Renamed

            _mockGetInfoEvent
                .Setup(x => x.Publish(It.IsAny<McpGetInfoEventArgs>())) // Renamed
                .Callback<McpGetInfoEventArgs>(args => capturedArgs = args); // Renamed

            // Act
            var task = _apiService.GetInfoAsync("test-tool");
            capturedArgs.ResultCompletionSource.SetResult(expectedResult);
            var result = await task;

            // Assert
            Assert.That(result, Is.EqualTo(expectedResult));
        }

        [Test]
        public void GetInfoAsync_SubscriberSetsException_ThrowsException()
        {
            // Arrange
            var expectedException = new Exception("Test error");
            McpGetInfoEventArgs capturedArgs = null; // Renamed

            _mockGetInfoEvent
                .Setup(x => x.Publish(It.IsAny<McpGetInfoEventArgs>())) // Renamed
                .Callback<McpGetInfoEventArgs>(args => capturedArgs = args); // Renamed

            // Act & Assert
            var task = _apiService.GetInfoAsync("test-tool");
            capturedArgs.ResultCompletionSource.SetException(expectedException);
            Assert.ThrowsAsync<Exception>(() => task);
        }
    }
}
