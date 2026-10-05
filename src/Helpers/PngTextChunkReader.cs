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
        private const long MaxInputBytes = 128L * 1024 * 1024;
        private const int MaxExpandedTextBytes = 4 * 1024 * 1024;
        private const int MaxTotalExpandedTextBytes = 8 * 1024 * 1024;
        private const int MaxChunkCount = 10000;

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
                if (new FileInfo(filePath).Length > MaxInputBytes)
                    return false;
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
            int chunkCount = 0;
            int totalExpandedTextBytes = 0;

            // チャンク列を走査: [長さ4B BE][タイプ4B ASCII][データ][CRC 4B]
            while (pos + 8 <= bytes.Length)
            {
                if (++chunkCount > MaxChunkCount)
                    return false;
                uint length = ReadBE32(bytes, pos);
                int dataStart = pos + 8;
                if ((ulong)dataStart + length + 4 > (ulong)bytes.Length)
                    break; // 破損チャンク

                string type = Encoding.ASCII.GetString(bytes, pos + 4, 4);
                if (type == "IEND")
                    break;

                if (type == "tEXt" || type == "zTXt" || type == "iTXt")
                {
                    if (length > MaxExpandedTextBytes + 1024)
                    {
                        return false;
                    }
                    int separator = Array.IndexOf(bytes, (byte)0, dataStart, (int)length);
                    if (IsValidKeyword(bytes, dataStart, separator) &&
                        result.ContainsKey(Encoding.Latin1.GetString(bytes, dataStart, separator - dataStart)))
                    {
                        pos = dataStart + (int)length + 4;
                        continue;
                    }
                    try
                    {
                        int remainingBudget = MaxTotalExpandedTextBytes - totalExpandedTextBytes;
                        string text = ParseTextChunk(type, bytes, dataStart, (int)length,
                            remainingBudget, out int expandedBytes, out bool exceededBudget);
                        // 破損して破棄するチャンクも、実際に展開した量を予算へ計上する。
                        totalExpandedTextBytes += expandedBytes;
                        if (exceededBudget)
                            return false;
                        if (!string.IsNullOrEmpty(text))
                        {
                            int keywordEnd = Array.IndexOf(bytes, (byte)0, dataStart, (int)length);
                            string keyword = Encoding.Latin1.GetString(bytes, dataStart, keywordEnd - dataStart);
                            if (!result.ContainsKey(keyword))
                            {
                                result[keyword] = text;
                            }
                        }
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

        private static string ParseTextChunk(string type, byte[] bytes, int start, int length,
            int remainingBudget, out int expandedBytes, out bool exceededBudget)
        {
            expandedBytes = 0;
            exceededBudget = false;
            int dataEnd = start + length;

            // キーワードは最初の NULL まで
            int keywordEnd = Array.IndexOf(bytes, (byte)0, start, length);
            if (!IsValidKeyword(bytes, start, keywordEnd))
                return null;

            string text = null;

            if (type == "tEXt")
            {
                // テキストは UTF-8 を優先し、失敗したら Latin-1
                int textStart = keywordEnd + 1;
                int textLength = dataEnd - textStart;
                if (textLength > MaxExpandedTextBytes) { exceededBudget = true; return null; }
                if (textLength > remainingBudget) { exceededBudget = true; return null; }
                expandedBytes = textLength;
                text = DecodeUtf8OrLatin1(bytes, textStart, textLength);
            }
            else if (type == "zTXt")
            {
                // NULL直後: 圧縮手法1B + zlib データ
                int compressedStart = keywordEnd + 2;
                if (keywordEnd + 1 < dataEnd && bytes[keywordEnd + 1] == 0)
                {
                    text = InflateZlib(bytes, compressedStart, dataEnd - compressedStart,
                        remainingBudget, out expandedBytes, out exceededBudget);
                }
            }
            else // iTXt
            {
                // NULL直後: 圧縮フラグ1B 圧縮手法1B 言語タグ\0 翻訳キーワード\0 テキスト(UTF-8)
                if (keywordEnd + 2 >= dataEnd || bytes[keywordEnd + 1] > 1 || bytes[keywordEnd + 2] != 0)
                    return null;
                bool compressed = bytes[keywordEnd + 1] == 1;
                int p = keywordEnd + 3;
                if (p > dataEnd) return null;
                int langEnd = Array.IndexOf(bytes, (byte)0, p, dataEnd - p);
                if (langEnd >= 0)
                {
                    int transEnd = Array.IndexOf(bytes, (byte)0, langEnd + 1, dataEnd - langEnd - 1);
                    if (transEnd >= 0)
                    {
                        int textStart = transEnd + 1;
                        int textLength = dataEnd - textStart;
                        if (compressed)
                            text = InflateZlib(bytes, textStart, textLength, remainingBudget,
                                out expandedBytes, out exceededBudget);
                        else if (textLength > MaxExpandedTextBytes)
                        {
                            exceededBudget = true;
                            return null;
                        }
                        else if (textLength <= remainingBudget)
                        {
                            expandedBytes = textLength;
                            text = DecodeUtf8OrLatin1(bytes, textStart, textLength);
                        }
                        else exceededBudget = true;
                    }
                }
            }

            return text;
        }

        private static bool IsValidKeyword(byte[] bytes, int start, int end)
        {
            int length = end - start;
            if (length < 1 || length > 79) return false;

            bool previousWasSpace = true;
            for (int i = start; i < end; i++)
            {
                byte value = bytes[i];
                bool isSpace = value == 32;
                if ((!isSpace && (value < 33 || value > 126) && (value < 161 || value > 255)) ||
                    (isSpace && previousWasSpace)) return false;
                previousWasSpace = isSpace;
            }
            return !previousWasSpace;
        }

        private static string InflateZlib(byte[] bytes, int start, int length, int maxOutputBytes,
            out int expandedBytes, out bool exceededBudget)
        {
            expandedBytes = 0;
            exceededBudget = false;
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
                var buffer = new byte[8192];
                int read;
                while ((read = deflate.Read(buffer, 0, buffer.Length)) != 0)
                {
                    expandedBytes += read;
                    if (output.Length + read > MaxExpandedTextBytes || output.Length + read > maxOutputBytes)
                    {
                        // 単チャンク超過でも処理全体を停止し、巨大チャンクの繰返し展開を防ぐ。
                        exceededBudget = true;
                        return null;
                    }
                    output.Write(buffer, 0, read);
                }
                expandedBytes = (int)output.Length;
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
