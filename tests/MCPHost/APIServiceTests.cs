using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Prism.Events;
using Illustra.MCPHost;
using Illustra.MCPHost.Events;

namespace Illustra.Tests.MCPHost
{
    [TestFixture]
    public class APIServiceTests
    {
        private Mock<IEventAggregator> _mockEventAggregator;
        private Mock<ExecuteToolEvent> _mockExecuteToolEvent;
        private Mock<GetInfoEvent> _mockGetInfoEvent;
        private APIService _apiService;

        [SetUp]
        public void Setup()
        {
            _mockEventAggregator = new Mock<IEventAggregator>();
            _mockExecuteToolEvent = new Mock<ExecuteToolEvent>();
            _mockGetInfoEvent = new Mock<GetInfoEvent>();

            _mockEventAggregator
                .Setup(x => x.GetEvent<ExecuteToolEvent>())
                .Returns(_mockExecuteToolEvent.Object);

            _mockEventAggregator
                .Setup(x => x.GetEvent<GetInfoEvent>())
                .Returns(_mockGetInfoEvent.Object);

            _apiService = new APIService(_mockEventAggregator.Object);
        }

        [Test]
        public async Task ExecuteToolAsync_ValidCall_PublishesCorrectEvent()
        {
            // Arrange
            var toolName = "test-tool";
            var parameters = new { param1 = "value1" };
            ExecuteToolEventArgs capturedArgs = null;

            _mockExecuteToolEvent
                .Setup(x => x.Publish(It.IsAny<ExecuteToolEventArgs>()))
                .Callback<ExecuteToolEventArgs>(args => capturedArgs = args);

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
            ExecuteToolEventArgs capturedArgs = null;

            _mockExecuteToolEvent
                .Setup(x => x.Publish(It.IsAny<ExecuteToolEventArgs>()))
                .Callback<ExecuteToolEventArgs>(args => capturedArgs = args);

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
            ExecuteToolEventArgs capturedArgs = null;

            _mockExecuteToolEvent
                .Setup(x => x.Publish(It.IsAny<ExecuteToolEventArgs>()))
                .Callback<ExecuteToolEventArgs>(args => capturedArgs = args);

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
            GetInfoEventArgs capturedArgs = null;

            _mockGetInfoEvent
                .Setup(x => x.Publish(It.IsAny<GetInfoEventArgs>()))
                .Callback<GetInfoEventArgs>(args => capturedArgs = args);

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
            GetInfoEventArgs capturedArgs = null;

            _mockGetInfoEvent
                .Setup(x => x.Publish(It.IsAny<GetInfoEventArgs>()))
                .Callback<GetInfoEventArgs>(args => capturedArgs = args);

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
            GetInfoEventArgs capturedArgs = null;

            _mockGetInfoEvent
                .Setup(x => x.Publish(It.IsAny<GetInfoEventArgs>()))
                .Callback<GetInfoEventArgs>(args => capturedArgs = args);

            // Act & Assert
            var task = _apiService.GetInfoAsync("test-tool");
            capturedArgs.ResultCompletionSource.SetException(expectedException);
            Assert.ThrowsAsync<Exception>(() => task);
        }
    }
}
