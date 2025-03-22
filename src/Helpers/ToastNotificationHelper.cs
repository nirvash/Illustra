using System;
using System.Windows;
using System.Windows.Threading;
using Illustra.Views;

namespace Illustra.Helpers
{
    public static class ToastNotificationHelper
    {
        /// <summary>
        /// トースト通知を表示します
        /// </summary>
        /// <param name="message">表示するメッセージ</param>
        /// <param name="durationSeconds">表示時間（秒）</param>
        public static void ShowAtScreenCorner(string message, double durationSeconds = 1.0)
        {
            var toast = new ToastWindow(message);
            toast.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            toast.Show();

            var durationMs = durationSeconds * 1000;
            Task.Delay((int)durationMs).ContinueWith(_ =>
            {
                toast.Dispatcher.Invoke(() => toast.Close());
            });
        }

        public static void ShowRelativeTo(FrameworkElement source, string message, double durationSeconds = 1.0)
        {
            // 所属するウィンドウを探す
            var window = Window.GetWindow(source);
            if (window == null)
            {
                // 見つからなければ画面右下に fallback
                ShowAtScreenCorner(message, durationSeconds);
                return;
            }

            var toast = new ToastWindow(message);
            toast.Owner = window; // フォーカス・Z順制御（任意）
            toast.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            toast.Show();

            var durationMs = durationSeconds * 1000;
            Task.Delay((int)durationMs).ContinueWith(_ =>
            {
                toast.Dispatcher.Invoke(() => toast.Close());
            });
        }
    }
}
