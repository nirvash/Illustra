using System;
using System.IO;
using System.Text;

namespace Illustra.Helpers
{
    /// <summary>
    /// 開発者モード時のみ、ビューアの静止画切替性能をファイルへ記録します。
    /// </summary>
    internal static class ViewerPerformanceLog
    {
        private static readonly object LockObject = new();
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Illustra",
            "viewer_performance.log");

        public static bool IsEnabled
        {
            get
            {
                try { return SettingsHelper.GetSettings().DeveloperMode; }
                catch { return false; }
            }
        }

        public static void Append(string message)
        {
            try
            {
                if (!IsEnabled) return;

                lock (LockObject)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                    File.AppendAllText(
                        LogFilePath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch
            {
                // 性能ログの失敗で画像表示を妨げない。
            }
        }
    }
}
