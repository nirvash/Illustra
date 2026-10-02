using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Illustra.Helpers
{
    /// <summary>開発者モードまたは環境変数指定時のビューア性能ログ。</summary>
    internal static class ViewerPerformanceLog
    {
        private static readonly string? OverridePath = Environment.GetEnvironmentVariable("ILLUSTRA_PERF_LOG");
        private static readonly string LogFilePath = string.IsNullOrWhiteSpace(OverridePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Illustra", "viewer_performance.log")
            : OverridePath;
        private static readonly AsyncLocal<long> RequestId = new();
        private static long _nextRequestId;
        private static readonly Channel<string> Pending = Channel.CreateBounded<string>(new BoundedChannelOptions(8192)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite
        });
        private static readonly Task Writer = Task.Run(async () =>
        {
            await foreach (var line in Pending.Reader.ReadAllAsync())
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                    await File.AppendAllTextAsync(LogFilePath, line, Encoding.UTF8);
                }
                catch { /* 計測失敗で表示を妨げない。 */ }
            }
        });

        public static long CurrentRequestId => RequestId.Value;
        public static bool IsEnabled
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(OverridePath)) return true;
                try { return SettingsHelper.GetSettings().DeveloperMode; }
                catch { return false; }
            }
        }

        public static IDisposable BeginRequest(string path)
        {
            var previous = RequestId.Value;
            RequestId.Value = Interlocked.Increment(ref _nextRequestId);
            Append($"switch-start path=\"{path}\"");
            return new RequestScope(previous);
        }

        private sealed class RequestScope(long previous) : IDisposable
        {
            public void Dispose() => RequestId.Value = previous;
        }

        public static void Append(string message)
        {
            if (!IsEnabled) return;
            // 時刻・相関情報は呼び出し側で採取し、ディスクIOは専用consumerへ渡す。
            Pending.Writer.TryWrite($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message} pid={Environment.ProcessId} request={CurrentRequestId} thread={Environment.CurrentManagedThreadId}{Environment.NewLine}");
        }
    }
}
