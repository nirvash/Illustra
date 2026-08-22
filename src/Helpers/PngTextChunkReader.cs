using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Illustra.Helpers
{
    /// <summary>
    /// PNG ファイルのテキストチャンク（tEXt / zTXt / iTXt）を読み出すリーダー。
    /// ComfyUI は生成設定を "prompt"（API形式グラフJSON）と
    /// "workflow"（GUI形式ワークフローJSON）の tEXt チャンクに埋め込む。
    /// </summary>
    public static class PngTextChunkReader
    {
        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>
        /// PNG ファイルからテキストチャンクをすべて読み出す。
        /// PNG として妥当でない場合やテキストチャンクが存在しない場合は false を返す。
        /// </summary>
        public static bool TryReadTags(string filePath, out Dictionary<string, string> tags)
        {
            tags = null;

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception)
            {
                return false;
            }

            if (bytes.Length < PngSignature.Length ||
                !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
                return false;

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            int pos = PngSignature.Length;

            // チャンク列を走査: [長さ4B BE][タイプ4B ASCII][データ][CRC 4B]
            while (pos + 8 <= bytes.Length)
            {
                uint length = ReadBE32(bytes, pos);
                int dataStart = pos + 8;
                if (dataStart + length + 4 > bytes.Length)
                    break; // 破損チャンク

                string type = Encoding.ASCII.GetString(bytes, pos + 4, 4);
                if (type == "IEND")
                    break;

                if (type == "tEXt" || type == "zTXt" || type == "iTXt")
                {
                    try
                    {
                        ParseTextChunk(type, bytes, dataStart, (int)length, result);
                    }
                    catch (Exception)
                    {
                        // 個別チャンクの解析失敗は無視して続行
                    }
                }

                pos = dataStart + (int)length + 4;
            }

            if (result.Count == 0)
                return false;

            tags = result;
            return true;
        }

        private static void ParseTextChunk(string type, byte[] bytes, int start, int length,
            Dictionary<string, string> result)
        {
            int dataEnd = start + length;

            // キーワードは最初の NULL まで
            int keywordEnd = Array.IndexOf(bytes, (byte)0, start, length);
            if (keywordEnd < 0)
                return;

            string keyword = Encoding.Latin1.GetString(bytes, start, keywordEnd - start);
            if (string.IsNullOrEmpty(keyword) || result.ContainsKey(keyword))
                return;

            string text = null;

            if (type == "tEXt")
            {
                // テキストは UTF-8 を優先し、失敗したら Latin-1
                text = DecodeUtf8OrLatin1(bytes, keywordEnd + 1, dataEnd - keywordEnd - 1);
            }
            else if (type == "zTXt")
            {
                // NULL直後: 圧縮手法1B + zlib データ
                int compressedStart = keywordEnd + 2;
                if (compressedStart < dataEnd && bytes[keywordEnd + 1] == 0)
                {
                    text = InflateZlib(bytes, compressedStart, dataEnd - compressedStart);
                }
            }
            else // iTXt
            {
                // NULL直後: 圧縮フラグ1B 圧縮手法1B 言語タグ\0 翻訳キーワード\0 テキスト(UTF-8)
                int p = keywordEnd + 3;
                bool compressed = p - 3 < dataEnd && bytes[keywordEnd + 1] == 1;
                int langEnd = Array.IndexOf(bytes, (byte)0, p, dataEnd - p);
                if (langEnd >= 0)
                {
                    int transEnd = Array.IndexOf(bytes, (byte)0, langEnd + 1, dataEnd - langEnd - 1);
                    if (transEnd >= 0)
                    {
                        int textStart = transEnd + 1;
                        text = compressed
                            ? InflateZlib(bytes, textStart, dataEnd - textStart)
                            : DecodeUtf8OrLatin1(bytes, textStart, dataEnd - textStart);
                    }
                }
            }

            if (!string.IsNullOrEmpty(text))
                result[keyword] = text;
        }

        private static string InflateZlib(byte[] bytes, int start, int length)
        {
            if (length <= 2) // zlibヘッダ2B + 最低1B
                return null;

            try
            {
                using var raw = new MemoryStream(bytes, start, length);
                // zlib ヘッダ (2バイト) をスキップして DeflateStream に渡す
                raw.ReadByte();
                raw.ReadByte();
                using var deflate = new System.IO.Compression.DeflateStream(raw,
                    System.IO.Compression.CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                return Encoding.UTF8.GetString(output.ToArray());
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string DecodeUtf8OrLatin1(byte[] bytes, int start, int length)
        {
            try
            {
                return Encoding.UTF8.GetString(bytes, start, length);
            }
            catch (Exception)
            {
                return Encoding.Latin1.GetString(bytes, start, length);
            }
        }

        /// <summary>
        /// JSON として妥当かどうかの簡易判定。
        /// </summary>
        public static bool IsJsonLike(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            var trimmed = value.TrimStart();
            if ((!trimmed.StartsWith("{") && !trimmed.StartsWith("[")))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(value);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static uint ReadBE32(byte[] b, int off) =>
            (uint)((b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3]);
    }
}
