using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Illustra.Helpers
{
    /// <summary>
    /// MP4 ファイルに埋め込まれたメタデータタグを読み取るクラス。
    /// ffmpeg が書き込む mdta スタイルのメタデータ
    /// （moov → udta → meta → hdlr/keys/ilst）を走査し、
    /// key 名と値の辞書を返す。外部ライブラリには依存しない。
    /// </summary>
    public static class Mp4MetadataReader
    {
        /// <summary>
        /// MP4 ファイルから埋め込みタグ（"prompt" / "workflow" 等）を読み取る
        /// </summary>
        /// <param name="filePath">MP4 ファイルパス</param>
        /// <param name="tags">key 名 → 値（UTF-8 文字列）の辞書</param>
        /// <returns>メタデータが読み取れた場合 true</returns>
        public static bool TryReadTags(string filePath, out Dictionary<string, string> tags)
        {
            tags = null;
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    return false;

                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BinaryReader(stream);

                // トップレベル box を走査して moov を探す
                long fileLength = stream.Length;
                long offset = 0;
                byte[] moovData = null;

                while (offset + 8 <= fileLength)
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                    if (!TryReadBoxHeader(reader, out uint size32, out string type, out ulong size64))
                        break;

                    ulong boxSize = size64 > 0 ? size64 : size32;
                    long headerSize = size64 > 0 ? 16 : 8;

                    // size == 0 はファイル末尾までを意味する
                    if (boxSize == 0)
                        boxSize = (ulong)(fileLength - offset);

                    if (boxSize < (ulong)headerSize || offset + (long)boxSize > fileLength)
                        break;

                    if (type == "moov")
                    {
                        stream.Seek(offset + headerSize, SeekOrigin.Begin);
                        moovData = reader.ReadBytes((int)(boxSize - (ulong)headerSize));
                        break;
                    }

                    offset += (long)boxSize;
                }

                if (moovData == null)
                    return false;

                return TryParseMoov(moovData, out tags);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mp4MetadataReader エラー ({filePath}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// moov box の内容から udta → meta → keys/ilst を解析する
        /// </summary>
        private static bool TryParseMoov(byte[] moovData, out Dictionary<string, string> tags)
        {
            tags = null;
            try
            {
                var children = EnumerateBoxes(moovData, 0, moovData.Length);

                byte[] metaPayload = null;
                foreach (var (type, payloadStart, payloadLength) in children)
                {
                    if (type == "udta")
                    {
                        var udtaChildren = EnumerateBoxes(moovData, payloadStart, payloadStart + payloadLength);
                        foreach (var (udtaType, udtaStart, udtaLength) in udtaChildren)
                        {
                            if (udtaType == "meta")
                            {
                                int start = udtaStart;
                                int length = udtaLength;
                                if (length >= 4 && IsMetaFullAtom(moovData, start))
                                {
                                    start += 4; // version / flags をスキップ
                                    length -= 4;
                                }
                                metaPayload = CopyRange(moovData, start, length);
                                break;
                            }
                        }
                    }
                    else if (type == "meta")
                    {
                        int start = payloadStart;
                        int length = payloadLength;
                        if (length >= 4 && IsMetaFullAtom(moovData, start))
                        {
                            start += 4; // version / flags をスキップ
                            length -= 4;
                        }
                        metaPayload = CopyRange(moovData, start, length);
                    }

                    if (metaPayload != null)
                        break;
                }

                if (metaPayload == null)
                    return false;

                return TryParseMeta(metaPayload, out tags);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// meta box 内から hdlr(mdta 確認) → keys → ilst を解析してタグ辞書を構築する
        /// </summary>
        private static bool TryParseMeta(byte[] data, out Dictionary<string, string> tags)
        {
            tags = null;
            List<string> keys = null;
            Dictionary<uint, string> values = null;

            foreach (var (type, payloadStart, payloadLength) in EnumerateBoxes(data, 0, data.Length))
            {
                switch (type)
                {
                    case "hdlr":
                        // ペイロード構成: version/flags(4) + pre_defined(4) + handler_type(4) + ...
                        // handler_type が 'mdta' 以外なら対象外
                        if (payloadLength >= 12 && ReadType(data, payloadStart + 8) != "mdta")
                            return false;
                        break;

                    case "keys":
                        keys = ParseKeys(data, payloadStart, payloadLength);
                        break;

                    case "ilst":
                        values = ParseIlst(data, payloadStart, payloadLength);
                        break;
                }
            }

            if (keys == null || values == null || keys.Count == 0)
                return false;

            tags = new Dictionary<string, string>();
            foreach (var kv in values)
            {
                uint index = kv.Key; // 1-based
                if (index >= 1 && index <= (uint)keys.Count)
                {
                    tags[keys[(int)index - 1]] = kv.Value;
                }
            }

            return tags.Count > 0;
        }

        /// <summary>
        /// keys atom を解析してキー名のリストを返す
        /// </summary>
        private static List<string> ParseKeys(byte[] data, int start, int length)
        {
            var result = new List<string>();
            if (length < 8)
                return result;

            int entryCount = ReadInt32BE(data, start + 4);
            int pos = start + 8;
            int end = start + length;

            for (int i = 0; i < entryCount && pos + 8 <= end; i++)
            {
                int entrySize = ReadInt32BE(data, pos);
                if (entrySize < 8 || pos + entrySize > end)
                    break;

                // namespace (4バイト、通常 'mdta') の後がキー名
                int nameLength = entrySize - 8;
                if (nameLength > 0)
                {
                    result.Add(Encoding.UTF8.GetString(data, pos + 8, nameLength));
                }
                else
                {
                    result.Add(string.Empty);
                }

                pos += entrySize;
            }

            return result;
        }

        /// <summary>
        /// ilst atom を解析して インデックス(1-based) → 値文字列 の辞書を返す
        /// </summary>
        private static Dictionary<uint, string> ParseIlst(byte[] data, int start, int length)
        {
            var result = new Dictionary<uint, string>();
            int pos = start;
            int end = start + length;

            while (pos + 8 <= end)
            {
                int itemSize = ReadInt32BE(data, pos);
                if (itemSize < 8 || pos + itemSize > end)
                    break;

                uint index = ReadUInt32BE(data, pos + 4); // box type 部が keys への 1-based インデックス

                // item 内の 'data' sub-box からペイロードを取得
                int childPos = pos + 8;
                int childEnd = pos + itemSize;
                while (childPos + 8 <= childEnd)
                {
                    int dataSize = ReadInt32BE(data, childPos);
                    if (dataSize < 16 || childPos + dataSize > childEnd)
                        break;

                    string dataType = ReadType(data, childPos + 4);
                    if (dataType == "data")
                    {
                        // type indicator(4) + locale(4) の後にペイロード
                        int payloadLength = dataSize - 16;
                        if (payloadLength > 0)
                        {
                            result[index] = Encoding.UTF8.GetString(data, childPos + 16, payloadLength);
                        }
                        break;
                    }

                    childPos += dataSize;
                }

                pos += itemSize;
            }

            return result;
        }

        /// <summary>
        /// バッファ内の指定範囲の子 box を列挙する
        /// </summary>
        private static List<(string Type, int PayloadStart, int PayloadLength)> EnumerateBoxes(
            byte[] data, int start, int end)
        {
            var boxes = new List<(string, int, int)>();
            int pos = start;

            while (pos + 8 <= end)
            {
                int size = ReadInt32BE(data, pos);
                if (size < 8 || pos + size > end)
                    break;

                string type = ReadType(data, pos + 4);
                int payloadStart = pos + 8;
                boxes.Add((type, payloadStart, size - 8));
                pos += size;
            }

            return boxes;
        }

        /// <summary>
        /// meta atom が full atom（version/flags ヘッダを持つ）かどうかを推定する。
        /// 直後 4 バイトが子 box のサイズとして不自然（< 8 または過大）なら full atom とみなす。
        /// </summary>
        private static bool IsMetaFullAtom(byte[] data, int payloadStart)
        {
            if (payloadStart + 4 >= data.Length)
                return false;

            uint candidateChildSize = ReadUInt32BE(data, payloadStart);
            return candidateChildSize < 8 || payloadStart + candidateChildSize > (uint)data.Length;
        }

        private static bool TryReadBoxHeader(BinaryReader reader, out uint size32, out string type, out ulong size64)
        {
            size32 = 0;
            type = string.Empty;
            size64 = 0;

            try
            {
                byte[] header = reader.ReadBytes(8);
                if (header.Length < 8)
                    return false;

                size32 = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
                type = Encoding.ASCII.GetString(header, 4, 4);

                if (size32 == 1)
                {
                    // largesize（64bit）
                    byte[] large = reader.ReadBytes(8);
                    if (large.Length < 8)
                        return false;
                    size64 = 0;
                    for (int i = 0; i < 8; i++)
                        size64 = (size64 << 8) | large[i];
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int ReadInt32BE(byte[] data, int offset)
        {
            return (int)ReadUInt32BE(data, offset);
        }

        private static uint ReadUInt32BE(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static string ReadType(byte[] data, int offset)
        {
            return Encoding.ASCII.GetString(data, offset, 4);
        }

        private static byte[] CopyRange(byte[] source, int start, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(source, start, result, 0, length);
            return result;
        }
    }
}
