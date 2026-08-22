using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Illustra.Tests.Helpers
{
    /// <summary>
    /// テスト用の最小構成 PNG を生成するビルダー。
    /// 実際の ComfyUI 出力と同じく、テキストチャンク (tEXt) に
    /// "prompt" / "workflow" を埋め込める。
    /// </summary>
    public static class TestPngBuilder
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>
        /// 1x1 のグレースケール PNG にテキストチャンクを埋め込んで返す。
        /// </summary>
        /// <param name="textChunks">埋め込む (キーワード, 値) のリスト</param>
        public static byte[] BuildPngWithTextChunks(params (string Key, string Value)[] textChunks)
        {
            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);

            // IHDR: 1x1 / 8bit / グレースケール(0)
            byte[] ihdr =
            {
                0x00, 0x00, 0x00, 0x01, // width = 1
                0x00, 0x00, 0x00, 0x01, // height = 1
                0x08,                   // bit depth
                0x00,                   // color type: grayscale
                0x00,                   // compression
                0x00,                   // filter
                0x00                    // interlace
            };
            WriteChunk(ms, "IHDR", ihdr);

            foreach (var (key, value) in textChunks)
            {
                WriteChunk(ms, "tEXt", EncodeTextChunk(key, value));
            }

            // IDAT: フィルタバイト(0) + ピクセル1個
            byte[] rawData = { 0x00, 0xFF };
            WriteChunk(ms, "IDAT", CompressZlib(rawData));

            WriteChunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        /// <summary>
        /// ComfyUI 形式の API グラフ JSON を持つ PNG を返す。
        /// </summary>
        public static byte[] BuildComfyUiPng(string promptJson, string workflowJson)
        {
            var chunks = new List<(string, string)>();
            if (promptJson != null)
                chunks.Add(("prompt", promptJson));
            if (workflowJson != null)
                chunks.Add(("workflow", workflowJson));
            return BuildPngWithTextChunks(chunks.ToArray());
        }

        /// <summary>
        /// テキストチャンクを持たない PNG を返す。
        /// </summary>
        public static byte[] BuildPlainPng()
        {
            return BuildPngWithTextChunks();
        }

        private static byte[] EncodeTextChunk(string key, string value)
        {
            var keyword = Encoding.Latin1.GetBytes(key);
            var text = Encoding.UTF8.GetBytes(value);
            var data = new byte[keyword.Length + 1 + text.Length];
            Array.Copy(keyword, data, keyword.Length);
            data[keyword.Length] = 0; // NULL セパレータ
            Array.Copy(text, 0, data, keyword.Length + 1, text.Length);
            return data;
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            stream.Write(new[]
            {
                (byte)(data.Length >> 24), (byte)(data.Length >> 16),
                (byte)(data.Length >> 8), (byte)data.Length
            }, 0, 4);
            stream.Write(typeBytes, 0, 4);
            stream.Write(data, 0, data.Length);

            uint crc = ComputeCrc32(typeBytes, data);
            stream.Write(new[]
            {
                (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc
            }, 0, 4);
        }

        private static uint ComputeCrc32(byte[] type, byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in type)
                crc = UpdateCrc(crc, b);
            foreach (byte b in data)
                crc = UpdateCrc(crc, b);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint UpdateCrc(uint crc, byte b)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                bool lsb = (crc & 1) != 0;
                crc >>= 1;
                if (lsb)
                    crc ^= 0xEDB88320u;
            }
            return crc;
        }

        private static byte[] CompressZlib(byte[] data)
        {
            using var ms = new MemoryStream();
            // zlib ヘッダ (CMF/FLG): 圧縮なし相当の 0x78 0x01
            ms.WriteByte(0x78);
            ms.WriteByte(0x01);
            using (var deflate = new System.IO.Compression.DeflateStream(ms,
                System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(data, 0, data.Length);
            }
            // Adler-32
            uint a = 1, bSum = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                bSum = (bSum + a) % 65521;
            }
            uint adler = (bSum << 16) | a;
            ms.WriteByte((byte)(adler >> 24));
            ms.WriteByte((byte)(adler >> 16));
            ms.WriteByte((byte)(adler >> 8));
            ms.WriteByte((byte)adler);
            return ms.ToArray();
        }
    }
}
