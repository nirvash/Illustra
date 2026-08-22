using System;
using System.Collections.Generic;
using System.Text;

namespace Illustra.Tests.Helpers
{
    /// <summary>
    /// テスト用に最小構成の MP4 バイト列（mdta メタデータ付き）を構築するヘルパー。
    /// 実ファイル（ffmpeg 出力）と同じ構造を持つ:
    /// ftyp → moov → udta → meta(full) → hdlr('mdta') + keys + ilst
    /// </summary>
    internal static class TestMp4Builder
    {
        /// <summary>
        /// タグ辞書から mdta メタデータ付き MP4 のバイト列を生成する
        /// </summary>
        public static byte[] BuildMp4WithMetadata(Dictionary<string, string> tags)
        {
            var keys = new List<string>(tags.Keys);

            // hdlr: handler_type = 'mdta'
            var hdlrPayload = new List<byte>();
            AppendUInt32(hdlrPayload, 0); // version / flags
            AppendUInt32(hdlrPayload, 0); // pre_defined
            hdlrPayload.AddRange(Encoding.ASCII.GetBytes("mdta"));
            for (int i = 0; i < 12; i++)
                hdlrPayload.Add(0); // reserved
            var hdlr = Box("hdlr", hdlrPayload.ToArray());

            // keys
            var keysPayload = new List<byte>();
            AppendUInt32(keysPayload, 0); // version / flags
            AppendUInt32(keysPayload, (uint)keys.Count);
            foreach (var key in keys)
            {
                var name = Encoding.UTF8.GetBytes(key);
                AppendUInt32(keysPayload, (uint)(8 + name.Length)); // entry size
                keysPayload.AddRange(Encoding.ASCII.GetBytes("mdta"));
                keysPayload.AddRange(name);
            }
            var keysBox = Box("keys", keysPayload.ToArray());

            // ilst: 各項目は box としてサイズヘッダを持ち、type 部が keys への 1-based インデックス
            var ilstChildren = new List<byte[]>();
            for (int i = 0; i < keys.Count; i++)
            {
                var payload = Encoding.UTF8.GetBytes(tags[keys[i]]);
                var dataBox = Box("data", UInt32Bytes(1), UInt32Bytes(0), payload); // type indicator = UTF-8, locale = 0

                var item = new List<byte>();
                AppendUInt32(item, (uint)(8 + dataBox.Length)); // 項目自身のサイズ
                AppendUInt32(item, (uint)(i + 1));              // type = インデックス
                item.AddRange(dataBox);
                ilstChildren.Add(item.ToArray());
            }
            var ilst = Box("ilst", ilstChildren.ToArray());

            // meta (full atom: version/flags を持つ)
            var meta = FullBox("meta", new[] { hdlr, keysBox, ilst });

            // udta / moov / ftyp
            var udta = Box("udta", meta);
            var moov = Box("moov", udta);
            var ftyp = Box("ftyp", Encoding.ASCII.GetBytes("isom"), UInt32Bytes(512), Encoding.ASCII.GetBytes("isom"));

            return Concat(ftyp, moov);
        }

        /// <summary>
        /// メタデータなしの MP4 風バイト列を生成する
        /// </summary>
        public static byte[] BuildMp4WithoutMetadata()
        {
            var ftyp = Box("ftyp", Encoding.ASCII.GetBytes("isom"), UInt32Bytes(512), Encoding.ASCII.GetBytes("isom"));
            var moov = Box("moov", Box("mvhd", new byte[100]));
            return Concat(ftyp, moov);
        }

        private static byte[] Box(string type, params byte[][] payloads)
        {
            int totalLength = 8;
            foreach (var payload in payloads)
                totalLength += payload?.Length ?? 0;

            var result = new List<byte>();
            AppendUInt32(result, (uint)totalLength);
            result.AddRange(Encoding.ASCII.GetBytes(type));
            foreach (var payload in payloads)
            {
                if (payload != null)
                    result.AddRange(payload);
            }

            return result.ToArray();
        }

        private static byte[] FullBox(string type, byte[][] children)
        {
            int totalLength = 12;
            foreach (var child in children)
                totalLength += child.Length;

            var result = new List<byte>();
            AppendUInt32(result, (uint)totalLength);
            result.AddRange(Encoding.ASCII.GetBytes(type));
            AppendUInt32(result, 0); // version / flags
            foreach (var child in children)
                result.AddRange(child);

            return result.ToArray();
        }

        private static byte[] UInt32Bytes(uint value)
        {
            return new[]
            {
                (byte)(value >> 24),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            };
        }

        private static void AppendUInt32(List<byte> list, uint value)
        {
            list.AddRange(UInt32Bytes(value));
        }

        private static byte[] Concat(params byte[][] arrays)
        {
            var result = new List<byte>();
            foreach (var array in arrays)
                result.AddRange(array);
            return result.ToArray();
        }
    }
}
