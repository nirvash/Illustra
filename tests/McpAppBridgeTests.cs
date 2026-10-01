using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Illustra.Events;
using Illustra.Mcp;
using NUnit.Framework;
using Prism.Events;

namespace Illustra.Tests
{
    [TestFixture]
    public class McpAppBridgeTests
    {
        private Dispatcher _dispatcher = null!;
        private Thread _thread = null!;
        private EventAggregator _events = null!;
        private McpAppBridge _bridge = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            var ready = new TaskCompletionSource<Dispatcher>();
            _thread = new Thread(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                ready.SetResult(dispatcher);
                Dispatcher.Run();
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _dispatcher = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _events = new EventAggregator();
            _bridge = new McpAppBridge(_events, _dispatcher);
        }

        [TearDown]
        public void TearDown()
        {
            _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            Assert.That(_thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        }

        [Test]
        public async Task Request_WaitsForTabLoad_AndSerializesRequestsAsync()
        {
            var prepared = new TaskCompletionSource<McpPrepareTabEventArgs>();
            int prepareCount = 0;
            int operationCount = 0;
            _events.GetEvent<McpPrepareTabEvent>().Subscribe(args =>
            {
                if (args.ValidateOnly)
                {
                    args.ResultCompletionSource!.SetResult(true);
                    return;
                }
                prepareCount++;
                args.ResolvedTabId = Guid.NewGuid();
                if (prepareCount == 1) prepared.SetResult(args);
                else args.ResultCompletionSource!.SetResult(true);
            });
            _events.GetEvent<McpGetFileListEvent>().Subscribe(args =>
            {
                operationCount++;
                args.ResultCompletionSource!.SetResult(true);
            });
            var firstArgs = new McpGetFileListEventArgs();
            var first = _bridge.PublishAndWaitAsync(firstArgs, ea => ea.GetEvent<McpGetFileListEvent>());
            var pending = await prepared.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = _bridge.PublishAndWaitAsync(new McpGetFileListEventArgs(), ea => ea.GetEvent<McpGetFileListEvent>());
            Assert.That(prepareCount, Is.EqualTo(1));
            Assert.That(operationCount, Is.Zero);
            Assert.That(first.IsCompleted, Is.False);
            pending.ResultCompletionSource!.SetResult(true);
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(firstArgs.ResolvedTabId, Is.EqualTo(pending.ResolvedTabId));
            Assert.That(operationCount, Is.EqualTo(2));
        }

        [Test]
        public async Task OpenFolder_WaitsForPostNavigationLoadAsync()
        {
            var pendingLoad = new TaskCompletionSource<McpPrepareTabEventArgs>();
            _events.GetEvent<McpPrepareTabEvent>().Subscribe(args =>
            {
                args.ResolvedTabId ??= Guid.NewGuid();
                if (args.WaitOnly) pendingLoad.SetResult(args);
                else args.ResultCompletionSource!.SetResult(true);
            });
            _events.GetEvent<McpOpenFolderEvent>().Subscribe(args => args.ResultCompletionSource!.SetResult(true));
            var request = _bridge.PublishAndWaitAsync(new McpOpenFolderEventArgs(), ea => ea.GetEvent<McpOpenFolderEvent>());
            var pending = await pendingLoad.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(request.IsCompleted, Is.False);
            pending.ResultCompletionSource!.SetResult(true);
            Assert.That(await request.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        }

        [Test]
        public void ChangedTab_BlocksOpenFolderBeforeAnyHandler()
        {
            bool opened = false;
            _events.GetEvent<McpPrepareTabEvent>().Subscribe(args =>
            {
                args.ResolvedTabId ??= Guid.NewGuid();
                if (args.ValidateOnly)
                    args.ResultCompletionSource!.SetException(new InvalidOperationException("Tab changed"));
                else args.ResultCompletionSource!.SetResult(true);
            });
            _events.GetEvent<McpOpenFolderEvent>().Subscribe(args =>
            {
                opened = true;
                args.ResultCompletionSource!.SetResult(true);
            });
            Assert.ThrowsAsync<InvalidOperationException>(() =>
                _bridge.PublishAndWaitAsync(new McpOpenFolderEventArgs(), ea => ea.GetEvent<McpOpenFolderEvent>()));
            Assert.That(opened, Is.False);
        }
    }
}
