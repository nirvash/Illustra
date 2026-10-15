using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Illustra.Tests.Views;

internal static class WpfTestDispatcher
{
    private static readonly Lazy<Dispatcher> Owner = new(CreateDispatcher);

    public static void Run(Action action) => Owner.Value.Invoke(action);

    private static Dispatcher CreateDispatcher()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Illustra WPF test dispatcher"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
