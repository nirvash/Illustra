using System;
using System.Threading.Tasks;
using Illustra.Events;
using Illustra.Mcp;
using Illustra.Mcp.Tools;
using NUnit.Framework;
using Prism.Events;

namespace Illustra.Tests
{
    [TestFixture]
    public class FileSelectionToolsTests
    {
        [Test]
        public async Task SelectFile_WhenFileIsInAnotherFolder_OpensParentFolderAndSelectsFileAsync()
        {
            var bridge = new CapturingMcpAppBridge { CurrentFolder = @"E:\FolderA" };
            var tools = new FileSelectionTools(bridge);
            var targetPath = @"E:\FolderB\image.png";

            var result = await tools.SelectFile([targetPath]);

            Assert.That(result.SelectedCount, Is.EqualTo(1));
            Assert.That(result.RequestedCount, Is.EqualTo(1));
            Assert.That(bridge.OpenFolderArgs, Is.Not.Null);
            Assert.That(bridge.OpenFolderArgs!.FolderPath, Is.EqualTo(@"E:\FolderB"));
            Assert.That(bridge.OpenFolderArgs.SelectedFilePath, Is.EqualTo(targetPath));
            Assert.That(bridge.SelectFilesArgs, Is.Null);
        }

        [Test]
        public async Task SelectFile_WhenFileIsInActiveFolder_UsesCurrentSelectionHandlerAsync()
        {
            var bridge = new CapturingMcpAppBridge { CurrentFolder = @"E:\FolderA" };
            var tools = new FileSelectionTools(bridge);
            var targetPath = @"E:\FolderA\image.png";

            var result = await tools.SelectFile([targetPath]);

            Assert.That(result.SelectedCount, Is.EqualTo(1));
            Assert.That(bridge.OpenFolderArgs, Is.Null);
            Assert.That(bridge.SelectFilesArgs?.Paths, Is.EqualTo(new[] { targetPath }));
        }

        private sealed class CapturingMcpAppBridge : IMcpAppBridge
        {
            public string? CurrentFolder { get; init; }
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
                        return Task.FromResult<object?>(selectFilesArgs.Paths.Count);
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