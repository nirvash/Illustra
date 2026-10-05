using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows;

namespace Illustra.Views;

/// <summary>Opt-in, privacy-safe asynchronous trace for external image drag routing.</summary>
internal static class ExternalImageDropTrace
{
    private const int VkEscape = 0x1B;
    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;
    private static readonly Channel<string>? Queue;
    private static readonly ConcurrentDictionary<string, long> LastSample = new();
    private static readonly string? LogPath;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    static ExternalImageDropTrace()
    {
        var configuredPath = Environment.GetEnvironmentVariable("ILLUSTRA_DROP_TRACE");
        if (string.IsNullOrWhiteSpace(configuredPath) || !Path.IsPathFullyQualified(configuredPath)) return;

        LogPath = configuredPath;
        Queue = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _ = Task.Run(WriteLoopAsync);
    }

    public static bool IsEnabled => Queue != null;

    public static void Write(string eventName, object? sender = null, RoutedEventArgs? args = null,
        FrameworkElement? overlay = null, string? reason = null, bool? escapeDown = null,
        bool? leftButtonDown = null, bool? rightButtonDown = null, bool? visibleBefore = null,
        bool? visibleAfter = null, FrameworkElement? pointerHost = null, Point? pointerInHost = null,
        bool? cursorPositionAvailable = null, bool rateLimited = false)
    {
        var queue = Queue;
        if (queue == null || (rateLimited && !ShouldSample(eventName))) return;

        try
        {
            var dragArgs = args as DragEventArgs;
            Point? position = null;
            if (dragArgs != null && overlay != null)
            {
                try { position = dragArgs.GetPosition(overlay); }
                catch { /* Trace must never affect drag routing. */ }
            }

            var record = new TraceRecord(
                DateTimeOffset.UtcNow,
                eventName,
                reason,
                DescribeType(sender),
                DescribeName(sender),
                DescribeType(args?.OriginalSource),
                DescribeName(args?.OriginalSource),
                position?.X,
                position?.Y,
                overlay?.ActualWidth,
                overlay?.ActualHeight,
                overlay?.Visibility.ToString(),
                pointerInHost?.X,
                pointerInHost?.Y,
                pointerHost?.ActualWidth,
                pointerHost?.ActualHeight,
                cursorPositionAvailable,
                escapeDown ?? IsDown(VkEscape),
                leftButtonDown ?? IsDown(VkLeftButton),
                rightButtonDown ?? IsDown(VkRightButton),
                dragArgs?.Handled,
                dragArgs?.Effects.ToString(),
                visibleBefore,
                visibleAfter);

            var json = JsonSerializer.Serialize(record);
            queue.Writer.TryWrite(json);
        }
        catch
        {
            // Diagnostics are best-effort and never participate in application behavior.
        }
    }

    private static bool ShouldSample(string eventName)
    {
        var now = Stopwatch.GetTimestamp();
        var previous = LastSample.GetOrAdd(eventName, 0);
        if (now - previous < Stopwatch.Frequency / 4) return false;
        return LastSample.TryUpdate(eventName, now, previous);
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static string? DescribeType(object? value) => value?.GetType().FullName;

    private static string? DescribeName(object? value) => value is FrameworkElement element
        ? element.Name
        : value is FrameworkContentElement contentElement ? contentElement.Name : null;

    private static async Task WriteLoopAsync()
    {
        try
        {
            var parent = Path.GetDirectoryName(LogPath!);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            await using var stream = new FileStream(LogPath!, FileMode.Append, FileAccess.Write, FileShare.ReadWrite,
                4096, FileOptions.Asynchronous);
            await using var writer = new StreamWriter(stream);
            await foreach (var line in Queue!.Reader.ReadAllAsync())
            {
                await writer.WriteLineAsync(line);
                await writer.FlushAsync();
            }
        }
        catch
        {
            // An unavailable log destination only disables trace output.
        }
    }

    private sealed record TraceRecord(DateTimeOffset Utc, string Event, string? Reason,
        string? SenderType, string? SenderName, string? OriginalSourceType, string? OriginalSourceName,
        double? OverlayX, double? OverlayY, double? OverlayWidth, double? OverlayHeight,
        string? OverlayVisibility, double? HostX, double? HostY, double? HostWidth, double? HostHeight,
        bool? CursorPositionAvailable, bool? EscapeDown, bool? LeftButtonDown, bool? RightButtonDown,
        bool? Handled, string? Effects, bool? VisibleBefore, bool? VisibleAfter);
}
