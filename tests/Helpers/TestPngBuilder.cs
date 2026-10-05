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

        public static byte[] BuildPngWithManyAncillaryChunks(int count)
        {
            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);
            WriteChunk(ms, "IHDR", BuildIhdr(1, 1, 0));
            for (int i = 0; i < count; i++) WriteChunk(ms, "vpAg", Array.Empty<byte>());
            WriteChunk(ms, "tEXt", EncodeTextChunk("prompt", "a cat"));
            WriteChunk(ms, "IDAT", CompressZlib(new byte[] { 0, 255 }));
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        public static byte[] BuildTwoFrameApng(string firstFrameText)
        {
            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);
            WriteChunk(ms, "IHDR", BuildIhdr(1, 1, 6));
            WriteChunk(ms, "tEXt", EncodeTextChunk("Description", firstFrameText));
            WriteChunk(ms, "tEXt", EncodeTextChunk("Software", "NovelAI"));
            WriteChunk(ms, "acTL", new byte[] { 0, 0, 0, 2, 0, 0, 0, 0 });
            WriteChunk(ms, "fcTL", BuildFrameControl(0));
            WriteChunk(ms, "IDAT", CompressZlib(new byte[] { 0, 255, 0, 0, 255 }));
            byte[] frameData = CompressZlib(new byte[] { 0, 0, 255, 0, 255 });
            var fdat = new byte[frameData.Length + 4];
            fdat[3] = 2;
            Array.Copy(frameData, 0, fdat, 4, frameData.Length);
            WriteChunk(ms, "fcTL", BuildFrameControl(1));
            WriteChunk(ms, "fdAT", fdat);
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        private static byte[] BuildFrameControl(uint sequence) => new byte[]
        {
            (byte)(sequence >> 24), (byte)(sequence >> 16), (byte)(sequence >> 8), (byte)sequence,
            0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 1, 0, 10, 0, 0
        };

        public static byte[] BuildPngWithTextChunk(string type, string key, string value,
            (string Key, string Value)? additional = null)
        {
            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);
            WriteChunk(ms, "IHDR", BuildIhdr(1, 1, 0));
            WriteChunk(ms, type, EncodeTypedTextChunk(type, key, value));
            if (additional.HasValue)
                WriteChunk(ms, "tEXt", EncodeTextChunk(additional.Value.Key, additional.Value.Value));
            WriteChunk(ms, "IDAT", CompressZlib(new byte[] { 0, 255 }));
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        public static byte[] BuildPngWithTypedTextChunks(params (string Type, string Key, string Value)[] chunks)
        {
            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);
            WriteChunk(ms, "IHDR", BuildIhdr(1, 1, 0));
            foreach (var (type, key, value) in chunks)
                WriteChunk(ms, type, EncodeTypedTextChunk(type, key, value));
            WriteChunk(ms, "IDAT", CompressZlib(new byte[] { 0, 255 }));
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        /// <summary>公式 nai_meta.py と同じ alpha 順・bit 順で SFW 合成 stealth_pngcomp PNG を作る。</summary>
        public static byte[] BuildStealthPng(string json, string description = null, bool withTextMetadata = false,
            bool invalidMagic = false, bool invalidLength = false, bool corruptGzip = false)
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var gzip = new System.IO.Compression.GZipStream(output,
                    System.IO.Compression.CompressionLevel.Optimal, true))
                    gzip.Write(jsonBytes, 0, jsonBytes.Length);
                compressed = output.ToArray();
            }
            if (corruptGzip) compressed[compressed.Length - 1] ^= 0xFF;
            byte[] magic = Encoding.UTF8.GetBytes("stealth_pngcomp");
            if (invalidMagic) magic[0] = (byte)'X';
            byte[] payload = new byte[magic.Length + 4 + compressed.Length];
            Array.Copy(magic, payload, magic.Length);
            int bitLength = invalidLength ? int.MaxValue & ~7 : compressed.Length * 8;
            payload[magic.Length] = (byte)(bitLength >> 24);
            payload[magic.Length + 1] = (byte)(bitLength >> 16);
            payload[magic.Length + 2] = (byte)(bitLength >> 8);
            payload[magic.Length + 3] = (byte)bitLength;
            Array.Copy(compressed, 0, payload, magic.Length + 4, compressed.Length);

            const int width = 128, height = 128;
            var rgba = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++) rgba[i * 4 + 3] = 254;
            for (int bitIndex = 0; bitIndex < payload.Length * 8; bitIndex++)
            {
                int x = bitIndex / height, y = bitIndex % height;
                int alphaIndex = (y * width + x) * 4 + 3;
                int bit = (payload[bitIndex / 8] >> (7 - bitIndex % 8)) & 1;
                rgba[alphaIndex] = (byte)(254 | bit);
            }

            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);
            WriteChunk(ms, "IHDR", BuildIhdr(width, height, 6));
            if (withTextMetadata)
            {
                WriteChunk(ms, "tEXt", EncodeTextChunk("Description", description ?? "text wins"));
                WriteChunk(ms, "tEXt", EncodeTextChunk("Software", "NovelAI"));
            }
            byte[] scanlines = new byte[height * (width * 4 + 1)];
            for (int y = 0; y < height; y++)
                Array.Copy(rgba, y * width * 4, scanlines, y * (width * 4 + 1) + 1, width * 4);
            WriteChunk(ms, "IDAT", CompressZlib(scanlines));
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        private static byte[] BuildIhdr(int width, int height, byte colorType) => new byte[]
        {
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
            8, colorType, 0, 0, 0
        };

        private static byte[] EncodeTypedTextChunk(string type, string key, string value)
        {
            byte[] keyword = Encoding.Latin1.GetBytes(key);
            byte[] text = Encoding.UTF8.GetBytes(value);
            byte[] body = text;
            if (type == "zTXt")
            {
                var compressed = CompressZlib(text);
                body = new byte[compressed.Length + 1];
                Array.Copy(compressed, 0, body, 1, compressed.Length);
            }
            if (type == "iTXt")
            {
                using var ms = new MemoryStream();
                ms.WriteByte(0); ms.WriteByte(0); // no compression
                ms.WriteByte(0); ms.WriteByte(0); // empty language, empty translated keyword
                ms.Write(text, 0, text.Length);
                body = ms.ToArray();
            }
            var result = new byte[keyword.Length + 1 + body.Length];
            Array.Copy(keyword, result, keyword.Length);
            Array.Copy(body, 0, result, keyword.Length + 1, body.Length);
            return result;
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
