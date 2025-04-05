using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Illustra.Helpers
{
    public class WindowVisibilityChecker
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// 指定されたウィンドウが他のウィンドウによって完全に覆われているかどうかを確認します。
        /// </summary>
        /// <param name="window">確認対象のウィンドウ。</param>
        /// <returns>ウィンドウが完全に覆われている場合は true、そうでない場合は false。</returns>
        public static bool IsWindowCovered(Window window)
        {
            if (window == null)
            {
                // ウィンドウが無効な場合は、安全のため覆われているとみなす
                return true;
            }

            // ウィンドウハンドルが取得できるか確認
            IntPtr hwnd;
            try
            {
                hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    // ハンドルが取得できない場合も覆われているとみなす
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                // ウィンドウがまだロードされていないなどの理由でハンドルが取得できない場合
                return true;
            }


            // フォアグラウンドウィンドウのハンドルを取得
            IntPtr foregroundHwnd = GetForegroundWindow();

            // 自分自身がフォアグラウンドなら表示されている (覆われていない)
            if (foregroundHwnd == hwnd)
                return false;

            // 自ウィンドウの矩形を取得
            if (!GetWindowRect(hwnd, out RECT myRect))
            {
                // 矩形が取得できない場合は覆われているとみなす
                return true;
            }

            // フォアグラウンドウィンドウの矩形を取得
            if (!GetWindowRect(foregroundHwnd, out RECT fgRect))
            {
                 // 矩形が取得できない場合は覆われているとみなす (予期せぬエラー)
                return true;
            }

            // 自ウィンドウがフォアグラウンドウィンドウに完全に覆われているかチェック
            // 完全に内包されている場合に true を返す
            bool isCovered =
                myRect.Left >= fgRect.Left && myRect.Right <= fgRect.Right &&
                myRect.Top >= fgRect.Top && myRect.Bottom <= fgRect.Bottom;

            return isCovered;
        }
    }
}
