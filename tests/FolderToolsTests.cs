using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Mcp;
using Illustra.Mcp.Tools;
using NUnit.Framework;
using Prism.Events;

namespace Illustra.Tests
{
    [TestFixture]
    public class FolderToolsTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public async Task OpenFolder_NormalizesSlashPathsBeforeSelectingMp4Async(bool selectFile)
        {
            var root = TestContext.CurrentContext.WorkDirectory;
            var folder = Path.Combine(root, "illustra-folder-test-" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "sample.mp4");
            File.WriteAllBytes(file, []);
            try
            {
                var bridge = new CapturingBridge();
                // OpenFolder does not access the database. Avoid initializing the user's real database.
                var database = (DatabaseManager)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseManager));
                var tools = new FolderTools(bridge, database);
                var requestedFolder = folder.Replace('\\', '/');
                var requestedFile = selectFile ? file.Replace('\\', '/') : string.Empty;
                var result = await tools.OpenFolder(requestedFolder, requestedFile);
                Assert.Multiple(() =>
                {
                    Assert.That(bridge.Args!.FolderPath, Is.EqualTo(Path.GetFullPath(folder)));
                    Assert.That(bridge.Args.SelectedFilePath, Is.EqualTo(selectFile ? Path.GetFullPath(file) : string.Empty));
                    Assert.That(result.Path, Is.EqualTo(requestedFolder), "Keep the caller's path spelling in the response");
                    Assert.That(result.Success, Is.True);
                });
            }
            finally { Directory.Delete(folder, true); }
        }

        private sealed class CapturingBridge : IMcpAppBridge
        {
            public McpOpenFolderEventArgs? Args { get; private set; }
            public Task<object?> PublishAndWaitAsync<TArgs>(TArgs args,
                Func<IEventAggregator, PubSubEvent<TArgs>> eventSelector, TimeSpan? timeout = null)
                where TArgs : McpBaseEventArgs
            {
                Args = (McpOpenFolderEventArgs)(object)args;
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
