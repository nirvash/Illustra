using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Illustra.Helpers;

/// <summary>
/// TIFF 内の既存値を移動せず、更新した IFD を末尾に追加する。
/// MakerNote・サムネイルなどの既存オフセットは変更しない。
/// </summary>
internal sealed class TiffUserCommentUpdater
{
    private const ushort ExifPointer = 0x8769;
    private const ushort UserComment = 0x9286;
    private const int MaxExifBytes = 8 * 1024 * 1024;
    private readonly byte[] _data;
    private readonly bool _littleEndian;
    private readonly HashSet<int> _visited = new();

    private TiffUserCommentUpdater(byte[] data)
    {
        _data = data;
        RequireRange(0, 8);
        _littleEndian = data[0] == 'I' && data[1] == 'I';
        if (!_littleEndian && !(data[0] == 'M' && data[1] == 'M'))
            throw InvalidExif();
        if (Read16(2) != 42) throw InvalidExif();
    }

    public static byte[] Update(byte[]? existing, string comment)
    {
        if (existing != null && existing.Length > MaxExifBytes) throw InvalidExif();
        // コメント自体は TIFF の byte order に関係なく UTF-16BE + BOM。
        // 既存 MetadataExtractor とアプリ内 BOM 判定の両方で読める。
        var text = new UnicodeEncoding(true, false, true).GetBytes(comment);
        var encoded = Encoding.ASCII.GetBytes("UNICODE\0").Concat(new byte[] { 0xfe, 0xff }).Concat(text).ToArray();
        var original = existing ?? new byte[] { (byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0 };
        var updater = new TiffUserCommentUpdater(original);
        var rootOffset = Offset(updater.Read32(4));
        updater.ValidateIfd(rootOffset, 0);
        var root = updater.ReadIfd(rootOffset);
        var pointers = root.Entries.Where(e => updater.Read16(e, 0) == ExifPointer).ToArray();
        if (pointers.Length > 1) throw InvalidExif();
        var exif = pointers.Length == 0
            ? new Ifd(new List<byte[]>(), 0)
            : updater.ReadIfd(Offset(updater.Read32(pointers[0], 8)));

        using var output = new MemoryStream();
        output.Write(original);
        Align(output);
        var valueOffset = checked((uint)output.Position);
        output.Write(encoded);
        Align(output);
        exif.Entries.RemoveAll(e => updater.Read16(e, 0) == UserComment);
        exif.Entries.Add(updater.Entry(UserComment, 7, (uint)encoded.Length, valueOffset));
        var exifOffset = checked((uint)output.Position);
        updater.WriteIfd(output, exif);
        root.Entries.RemoveAll(e => updater.Read16(e, 0) == ExifPointer);
        root.Entries.Add(updater.Entry(ExifPointer, 4, 1, exifOffset));
        var newRootOffset = checked((uint)output.Position);
        updater.WriteIfd(output, root);
        if (output.Length > MaxExifBytes) throw InvalidExif();
        var result = output.ToArray();
        updater.Write32(result.AsSpan(4, 4), newRootOffset);
        return result;
    }

    private sealed record Ifd(List<byte[]> Entries, uint Next);

    private Ifd ReadIfd(int offset)
    {
        if (offset < 8) throw InvalidExif();
        RequireRange(offset, 2);
        var count = Read16(offset);
        RequireRange(offset + 2, count * 12 + 4);
        var entries = new List<byte[]>(count);
        for (var i = 0; i < count; i++) entries.Add(_data.AsSpan(offset + 2 + 12 * i, 12).ToArray());
        return new Ifd(entries, Read32(offset + 2 + 12 * count));
    }

    private void ValidateIfd(int offset, int depth)
    {
        // 深い/循環 IFD は安全に拒否。既存データを推測して修復しない。
        if (depth > 16 || _visited.Count >= 1024 || !_visited.Add(offset)) throw InvalidExif();
        var ifd = ReadIfd(offset);
        var tags = new HashSet<ushort>();
        foreach (var entry in ifd.Entries)
        {
            var tag = Read16(entry, 0);
            if (!tags.Add(tag)) throw InvalidExif();
            var type = Read16(entry, 2);
            var unitSize = type switch
            {
                1 or 2 or 6 or 7 => 1,
                3 or 8 => 2,
                4 or 9 or 11 or 13 => 4,
                5 or 10 or 12 => 8,
                _ => throw InvalidExif()
            };
            var count = Read32(entry, 4);
            var length = (long)unitSize * count;
            if (length > MaxExifBytes) throw InvalidExif();
            var valueOffset = length > 4 ? Offset(Read32(entry, 8)) : -1;
            if (length > 4)
            {
                if (valueOffset < 8) throw InvalidExif();
                RequireRange(valueOffset, (int)length);
            }
            if (tag is ExifPointer or 0x8825 or 0xa005)
            {
                if (type != 4 || count != 1) throw InvalidExif();
                ValidateIfd(Offset(Read32(entry, 8)), depth + 1);
            }
            else if (tag == 0x014a) // TIFF SubIFDs
            {
                if (type != 4 && type != 13) throw InvalidExif();
                if (count > 128) throw InvalidExif();
                for (var i = 0; i < count; i++)
                    ValidateIfd(Offset(count == 1 ? Read32(entry, 8) : Read32(valueOffset + i * 4)), depth + 1);
            }
        }
        // JPEG thumbnail data are pointed to by separate offset/length entries.
        var thumbnailOffset = ifd.Entries.FirstOrDefault(e => Read16(e, 0) == 0x0201);
        var thumbnailLength = ifd.Entries.FirstOrDefault(e => Read16(e, 0) == 0x0202);
        if (thumbnailOffset != null || thumbnailLength != null)
        {
            if (thumbnailOffset == null || thumbnailLength == null || Read16(thumbnailOffset, 2) != 4 ||
                Read32(thumbnailOffset, 4) != 1 || Read16(thumbnailLength, 2) != 4 || Read32(thumbnailLength, 4) != 1)
                throw InvalidExif();
            var imageOffset = Offset(Read32(thumbnailOffset, 8));
            if (imageOffset < 8) throw InvalidExif();
            RequireRange(imageOffset, Offset(Read32(thumbnailLength, 8)));
        }
        if (ifd.Next != 0) ValidateIfd(Offset(ifd.Next), depth + 1);
    }

    private void WriteIfd(Stream stream, Ifd ifd)
    {
        if (ifd.Entries.Count > ushort.MaxValue) throw InvalidExif();
        var result = new byte[2 + ifd.Entries.Count * 12 + 4];
        Write16(result, (ushort)ifd.Entries.Count);
        var sorted = ifd.Entries.OrderBy(e => Read16(e, 0)).ToArray();
        for (var i = 0; i < sorted.Length; i++) sorted[i].CopyTo(result, 2 + i * 12);
        Write32(result.AsSpan(result.Length - 4), ifd.Next);
        stream.Write(result);
    }

    private byte[] Entry(ushort tag, ushort type, uint count, uint offset)
    {
        var bytes = new byte[12];
        Write16(bytes, tag);
        Write16(bytes.AsSpan(2), type);
        Write32(bytes.AsSpan(4), count);
        Write32(bytes.AsSpan(8), offset);
        return bytes;
    }

    private ushort Read16(int offset) => Read16(_data, offset);
    private ushort Read16(byte[] data, int offset) => _littleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2))
        : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
    private uint Read32(int offset) => Read32(_data, offset);
    private uint Read32(byte[] data, int offset) => _littleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4))
        : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private void Write16(Span<byte> data, ushort value)
    {
        if (_littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(data, value);
        else BinaryPrimitives.WriteUInt16BigEndian(data, value);
    }

    private void Write32(Span<byte> data, uint value)
    {
        if (_littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        else BinaryPrimitives.WriteUInt32BigEndian(data, value);
    }

    private void RequireRange(int offset, int length)
    {
        if (offset < 8 && offset != 0 || offset < 0 || length < 0 || offset > _data.Length - length)
            throw InvalidExif();
    }

    private static int Offset(uint value) => value > int.MaxValue ? throw InvalidExif() : (int)value;
    private static void Align(Stream stream) { if ((stream.Position & 1) != 0) stream.WriteByte(0); }
    private static InvalidDataException InvalidExif() => new("EXIF TIFF が破損しているか、安全に編集できない構造です。");
}
