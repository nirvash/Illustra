using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Illustra.Helpers
{
    /// <summary>
    /// WebP画像に関するヘルパーメソッドを提供します。
    /// </summary>
    public static class WebPHelper
    {
        /// <summary>
        /// 指定されたWebP画像ファイルがアニメーションを含むかどうかを判定します。
        /// </summary>
        /// WebPファイルのヘッダーを読み取り、アニメーションかどうかを判定します (軽量版)。
        /// </summary>
        public static async Task<bool> IsAnimatedWebPAsync(string filePath)
        {
            const int headerSize = 30; // RIFF(4) + Size(4) + WEBP(4) + VP8X Chunk Header(8) + Flags(1) + α (余裕をもたせる)
            byte[] buffer = new byte[headerSize];

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, headerSize);
                    if (bytesRead < 21) // RIFF + Size + WEBP + VP8X Header + Flags の最小サイズ
                    {
                        return false; // ヘッダーが短すぎる
                    }
                }

                // RIFFヘッダーチェック
                if (buffer[0] != 'R' || buffer[1] != 'I' || buffer[2] != 'F' || buffer[3] != 'F')
                    return false;
                // WEBPフォーマットチェック
                if (buffer[8] != 'W' || buffer[9] != 'E' || buffer[10] != 'B' || buffer[11] != 'P')
                    return false;
                // VP8Xチャンクチェック
                if (buffer[12] != 'V' || buffer[13] != 'P' || buffer[14] != '8' || buffer[15] != 'X')
                    return false; // VP8Xチャンクがない場合は非アニメーション (または非拡張フォーマット)

                // VP8Xチャンクのフラグバイト (オフセット20)
                byte flags = buffer[20];

                // アニメーションフラグ (A) は2番目のビット (0x02)
                bool isAnimation = (flags & 0x02) != 0;

                return isAnimation;
            }
            catch
            {
                // エラー時は判定不能として false を返す
                return false;
            }
        }
    }
}
