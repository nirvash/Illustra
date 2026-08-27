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

        [Test]
        public async Task ShowViewer_WhenFileIsInAnotherFolder_OpensParentFolderAndSelectsFileAsync()
        {
            var bridge = new CapturingMcpAppBridge { CurrentFolder = @"E:\FolderA" };
            var tools = new ViewerTools(bridge);

            await tools.ShowViewer(_tempFilePath);

            Assert.That(bridge.OpenFolderArgs, Is.Not.Null);
            Assert.That(bridge.OpenFolderArgs!.FolderPath, Is.EqualTo(Path.GetDirectoryName(_tempFilePath)));
            Assert.That(bridge.OpenFolderArgs.SelectedFilePath, Is.EqualTo(_tempFilePath));
            Assert.That(bridge.SelectFilesArgs, Is.Null);
        }

        [Test]
        public void ShowViewer_WhenFileSelectionFails_DoesNotShowViewer()
        {
            var bridge = new CapturingMcpAppBridge
            {
                CurrentFolder = Path.GetDirectoryName(_tempFilePath),
                SelectedCount = 0
            };
            var tools = new ViewerTools(bridge);

            Assert.ThrowsAsync<InvalidOperationException>(() => tools.ShowViewer(_tempFilePath));
            Assert.That(bridge.ShowViewerArgs, Is.Null);
        }
        private sealed class CapturingMcpAppBridge : IMcpAppBridge
        {
            public string? CurrentFolder { get; init; }
            public int SelectedCount { get; init; } = 1;
            public McpShowViewerEventArgs? ShowViewerArgs { get; private set; }
            public McpOpenFolderEventArgs? OpenFolderArgs { get; private set; }
            public McpSelectFilesEventArgs? SelectFilesArgs { get; private set; }

            public Task<object?> PublishAndWaitAsync<TArgs>(
                TArgs args,
                Func<IEventAggregator, PubSubEvent<TArgs>> eventSelector,
                TimeSpan? timeout = null)
                where TArgs : McpBaseEventArgs
            {
                switch (args)
                {
                    case McpGetAppStatusEventArgs statusArgs:
                        statusArgs.CurrentFolder = CurrentFolder;
                        return Task.FromResult<object?>(true);
                    case McpOpenFolderEventArgs openFolderArgs:
                        OpenFolderArgs = openFolderArgs;
                        return Task.FromResult<object?>(true);
                    case McpSelectFilesEventArgs selectFilesArgs:
                        SelectFilesArgs = selectFilesArgs;
                        return Task.FromResult<object?>(SelectedCount);
                    case McpShowViewerEventArgs showViewerArgs:
                        ShowViewerArgs = showViewerArgs;
                        return Task.FromResult<object?>(true);
                    default:
                        throw new InvalidOperationException($"Unexpected event type: {typeof(TArgs).Name}");
                }
            }

            public Task InvokeOnUiThreadAsync(Action action)
            {
                action();
                return Task.CompletedTask;
            }
        }
    }
}