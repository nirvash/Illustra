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

        [TestCase("mcp")]
        [TestCase("active")]
        public async Task SelectFile_PreservesTargetAcrossFolderNavigationAsync(string targetTab)
        {
            var bridge = new CapturingMcpAppBridge { CurrentFolder = @"E:\FolderA" };
            var tools = new FileSelectionTools(bridge);
            await tools.SelectFile([@"E:\FolderB\image.png"], targetTab: targetTab);
            Assert.That(bridge.OpenFolderArgs!.TargetTab, Is.EqualTo(targetTab));
            Assert.That(bridge.OpenFolderArgs.ResolvedTabId, Is.EqualTo(bridge.TabId));
        }

        [Test]
        public void InvalidTarget_IsRejectedBeforeUiRequests()
        {
            var bridge = new CapturingMcpAppBridge();
            var tools = new FileSelectionTools(bridge);
            Assert.ThrowsAsync<ArgumentException>(() => tools.SelectFile([@"E:\FolderB\image.png"], targetTab: "other"));
            Assert.That(bridge.OpenFolderArgs, Is.Null);
        }

        private sealed class CapturingMcpAppBridge : IMcpAppBridge
        {
            public Guid TabId { get; } = Guid.NewGuid();
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
                        statusArgs.ResolvedTabId = TabId;
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