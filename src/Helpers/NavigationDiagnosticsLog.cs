using System;
using System.IO;
using System.Text;

namespace Illustra.Helpers
{
    /// <summary>
    /// タブのパス移動不具合（Issue #48）解析用の診断ログ。
    /// 開発者モードが有効なときのみ %APPDATA%\Illustra\navigation_diag.log に追記し、5MB 超えたら先頭を切り詰める。
    /// 例外は一切外部に出さない（本体動作に影響させない）。
    /// 原因特定後は削除する。
    /// </summary>
    public static class NavigationDiagnosticsLog
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Illustra",
            "navigation_diag.log");

        private const long MaxFileSizeBytes = 5 * 1024 * 1024;
        private static readonly object _lock = new object();

        // 設定ロード中の FolderPath セッター経由での再入（無限再帰）を防ぐ
        [ThreadStatic]
        private static bool _inAppend;

        public static void Append(string message, bool withStackTrace = false)
        {
            try
            {
                if (_inAppend) return;
                _inAppend = true;
                try
                {
                    AppendCore(message, withStackTrace);
                }
                finally
                {
                    _inAppend = false;
                }
            }
            catch
            {
                // 診断ログは失敗しても無視する
            }
        }

        private static void AppendCore(string message, bool withStackTrace)
        {
            try
            {
                if (!SettingsHelper.GetSettings().DeveloperMode) return;

                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.Append(" [");
                sb.Append(Environment.CurrentManagedThreadId);
                sb.Append("] ");
                sb.AppendLine(message);

                if (withStackTrace)
                {
                    sb.AppendLine("  --- stack ---");
                    foreach (var line in Environment.StackTrace.Split('\n'))
                    {
                        var trimmed = line.TrimEnd('\r');
                        if (trimmed.Contains("NavigationDiagnosticsLog")) continue;
                        sb.AppendLine("  " + trimmed.Trim());
                    }
                    sb.AppendLine("  --- end stack ---");
                }

                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(LogFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxFileSizeBytes)
                    {
                        // 先頭半分を切り捨てて容量を抑える
                        var content = File.ReadAllText(LogFilePath);
                        var keep = content.Substring(content.Length / 2);
                        File.WriteAllText(LogFilePath, "-- truncated --\n" + keep);
                    }

                    File.AppendAllText(LogFilePath, sb.ToString());
                }
            }
            catch
            {
                // 診断ログは失敗しても無視する
            }
        }
    }
}
