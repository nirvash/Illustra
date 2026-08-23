using System;
using System.IO;
using System.Threading.Tasks;
using Illustra.Events;
using Illustra.Mcp;
using Illustra.Mcp.Tools;
using NUnit.Framework;
using Prism.Events;

namespace Illustra.Tests
{
    [TestFixture]
    public class ViewerToolsTests
    {
        private string _tempFilePath = null!;

        [SetUp]
        public void SetUp()
        {
            _tempFilePath = Path.GetTempFileName();
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFilePath))
            {
                File.Delete(_tempFilePath);
            }
        }

        [Test]
        public async Task ShowViewer_WhenBringToFrontIsOmitted_DefaultsToTrueAsync()
        {
            var bridge = new CapturingMcpAppBridge();
            var tools = new ViewerTools(bridge);

            await tools.ShowViewer(_tempFilePath);

            Assert.That(bridge.ShowViewerArgs, Is.Not.Null);
            Assert.That(bridge.ShowViewerArgs!.BringToFront, Is.True);
        }

        [Test]
        public async Task ShowViewer_WhenBringToFrontIsFalse_PassesFalseToUiHandlerAsync()
        {
            var bridge = new CapturingMcpAppBridge();
            var tools = new ViewerTools(bridge);

            await tools.ShowViewer(_tempFilePath, bringToFront: false);

            Assert.That(bridge.ShowViewerArgs, Is.Not.Null);
            Assert.That(bridge.ShowViewerArgs!.BringToFront, Is.False);
        }

        private sealed class CapturingMcpAppBridge : IMcpAppBridge
        {
            public McpShowViewerEventArgs? ShowViewerArgs { get; private set; }

            public Task<object?> PublishAndWaitAsync<TArgs>(
                TArgs args,
                Func<IEventAggregator, PubSubEvent<TArgs>> eventSelector,
                TimeSpan? timeout = null)
                where TArgs : McpBaseEventArgs
            {
                ShowViewerArgs = args as McpShowViewerEventArgs;
                return Task.FromResult<object?>(true);
            }

            public Task InvokeOnUiThreadAsync(Action action)
            {
                action();
                return Task.CompletedTask;
            }
        }
    }
}
