using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Illustra.Helpers;

/// <summary>
/// JPEG/PNG/WebP の画像データを再エンコードせず UserComment を更新する。
/// 形式が不正/未対応のときは保存前に拒否し、原本を保持する。
/// </summary>
public static class ExifUserCommentWriter
{
    private const int MaxFileBytes = 256 * 1024 * 1024;
    private const int MaxCommentChars = 32767;
    private const int MaxChunks = 65536;
    private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
    private static readonly byte[] ExifSignature = Encoding.ASCII.GetBytes("Exif\0\0");
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static async Task SaveAsync(string path, string comment)
    {
        var fullPath = Path.GetFullPath(path);
        byte[] original;
        // 書込み共有を許可せず、長さ確認後に全読取りを終えてから出力を作る。
        using (var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
        {
            if (input.Length > MaxFileBytes) throw InvalidImage();
            original = new byte[checked((int)input.Length)];
            await input.ReadExactlyAsync(original).ConfigureAwait(false);
        }
        var updated = Rewrite(original, comment);
        var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                await output.WriteAsync(updated).ConfigureAwait(false);
                output.Flush(true);
            }
            // commit自体は同期処理。Mutex ownershipをawaitを跨いで持たず、UI threadも待機させない。
            await Task.Run(() => CommitPrepared(fullPath, original, tempPath)).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    // 保存の準備とcommitの境界。置換delegateは合成fixtureで競合を再現するinternal seam。
    internal static void CommitPrepared(string path, byte[] original, string tempPath,
        Action<string, string>? replaceForTest = null, int mutexTimeoutMilliseconds = 10000)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            using var mutex = new Mutex(false, GetCommitMutexName(fullPath));
            var ownsMutex = false;
            try
            {
                try { ownsMutex = mutex.WaitOne(mutexTimeoutMilliseconds); }
                catch (AbandonedMutexException)
                {
                    // 先行instanceの終了で取得されたownership。全byteを再比較してから保存する。
                    ownsMutex = true;
                }
                if (!ownsMutex) throw new TimeoutException("同じ画像の保存処理を待機しています。再試行してください。");

                // 同じcanonical pathとMutexを使う更新済みIllustraのcommitを直列化する。
                // lock外で準備したsnapshotが古くなっていれば、長さと全byte比較で拒否する。
                using (var current = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    if (current.Length != original.Length) throw new IOException("保存前に画像ファイルが変更されました。");
                    var buffer = new byte[65536];
                    for (var offset = 0; offset < original.Length;)
                    {
                        var count = Math.Min(buffer.Length, original.Length - offset);
                        current.ReadExactly(buffer.AsSpan(0, count));
                        if (!buffer.AsSpan(0, count).SequenceEqual(original.AsSpan(offset, count)))
                            throw new IOException("保存前に画像ファイルが変更されました。");
                        offset += count;
                    }
                }
                // 比較handleを閉じて、同じdirectoryの一時ファイルを原子的に置換する。
                // 非協調の外部writer（rename等）の検出はbest effortで、完全CASではない。
                // File.Replaceの原子性とは別の制約。非原子的な上書きにはfallbackしない。
                if (replaceForTest != null) replaceForTest(tempPath, fullPath);
                else File.Replace(tempPath, fullPath, null);
            }
            finally
            {
                if (ownsMutex) mutex.ReleaseMutex();
            }
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static string GetCommitMutexName(string path)
    {
        // drive/相対成分/区切り/caseを正規化。symlink・hardlink別名の同一性までは解決しない。
        var canonicalPath = Path.GetFullPath(path)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToUpperInvariant();
        // Global namespaceはWindows session間でも共有。アクセスできなければ保存前に安全に失敗する。
        return @"Global\Illustra.ExifUserComment." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPath)));
    }

    public static byte[] Rewrite(byte[] original, string comment)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(comment);
        if (comment.Length > MaxCommentChars) throw new ArgumentException("コメントが長すぎます。", nameof(comment));
        if (original.Length > MaxFileBytes) throw InvalidImage();
        byte[] result;
        if (original.AsSpan().StartsWith(PngSignature)) result = RewritePng(original, comment);
        else if (original.Length >= 12 && IsName(original, 0, "RIFF") && IsName(original, 8, "WEBP"))
            result = RewriteWebp(original, comment);
        else if (original.Length >= 4 && original[0] == 0xff && original[1] == 0xd8) result = RewriteJpeg(original, comment);
        else throw InvalidImage();
        if (result.Length > MaxFileBytes) throw InvalidImage();
        return result;
    }

    private sealed record Chunk(int Offset, int Size, string Name)
    {
        public int PngLength => Size + 12;
        public int WebpLength => Size + 8 + (Size & 1);
    }

    private static byte[] RewritePng(byte[] original, string comment)
    {
        var chunks = new List<Chunk>();
        var foundData = false;
        var foundEnd = false;
        for (var offset = 8; offset < original.Length;)
        {
            if (chunks.Count >= MaxChunks) throw InvalidImage();
            RequireRange(original, offset, 12);
            var size = CheckedLength(BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(offset, 4)));
            RequireRange(original, offset, checked(size + 12));
            var name = Encoding.ASCII.GetString(original, offset + 4, 4);
            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(offset + size + 8, 4));
            if (Crc(original.AsSpan(offset + 4, size + 4)) != storedCrc) throw InvalidImage();
            if (chunks.Count == 0 && (name != "IHDR" || size != 13)) throw InvalidImage();
            if (name == "IHDR" && chunks.Count != 0) throw InvalidImage();
            if (name == "IDAT") foundData = true;
            chunks.Add(new Chunk(offset, size, name));
            offset += size + 12;
            if (name == "IEND")
            {
                if (size != 0 || offset != original.Length) throw InvalidImage();
                foundEnd = true;
                break;
            }
        }
        if (!foundData || !foundEnd || BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(16, 4)) == 0 ||
            BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(20, 4)) == 0) throw InvalidImage();
        var existing = SingleExif(chunks, "eXIf");
        var tiff = TiffUserCommentUpdater.Update(existing == null ? null : original.AsSpan(existing.Offset + 8, existing.Size).ToArray(), comment);
        using var output = new MemoryStream();
        output.Write(PngSignature);
        var inserted = false;
        foreach (var chunk in chunks)
        {
            if (chunk.Name == "eXIf") continue;
            if (!inserted && chunk.Name == "IDAT")
            {
                WritePngExif(output, tiff);
                inserted = true;
            }
            output.Write(original, chunk.Offset, chunk.PngLength);
        }
        return output.ToArray();
    }

    private static byte[] RewriteJpeg(byte[] original, string comment)
    {
        if (original[^2] != 0xff || original[^1] != 0xd9) throw InvalidImage();
        var segments = new List<Chunk>();
        var offset = 2;
        var scanOffset = -1;
        var foundFrame = false;
        while (offset < original.Length - 2)
        {
            if (segments.Count >= MaxChunks) throw InvalidImage();
            var start = offset;
            if (original[offset++] != 0xff) throw InvalidImage();
            while (offset < original.Length && original[offset] == 0xff) offset++;
            RequireRange(original, offset, 3);
            var marker = original[offset++];
            if (marker is 0 or 0xd8 or 0xd9 or 0x01 or >= 0xd0 and <= 0xd7) throw InvalidImage();
            var size = BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(offset, 2));
            if (size < 2) throw InvalidImage();
            RequireRange(original, offset, size);
            if (marker == 0xda)
            {
                scanOffset = start;
                break;
            }
            if (marker is >= 0xc0 and <= 0xcf && marker is not 0xc4 and not 0xc8 and not 0xcc) foundFrame = true;
            var isExif = marker == 0xe1 && size >= 8 && original.AsSpan(offset + 2, 6).SequenceEqual(ExifSignature);
            segments.Add(new Chunk(start, offset + size - start, isExif ? "EXIF" : "other"));
            offset += size;
        }
        if (scanOffset < 0 || !foundFrame) throw InvalidImage();
        var existing = SingleExif(segments, "EXIF");
        byte[]? oldTiff = null;
        if (existing != null)
        {
            // marker fill bytes are permitted; find its actual length field.
            var lengthOffset = existing.Offset + 1;
            while (original[lengthOffset] == 0xff) lengthOffset++;
            lengthOffset++;
            oldTiff = original.AsSpan(lengthOffset + 8, existing.Offset + existing.Size - lengthOffset - 8).ToArray();
        }
        var tiff = TiffUserCommentUpdater.Update(oldTiff, comment);
        if (tiff.Length + 8 > ushort.MaxValue) throw new InvalidDataException("JPEG EXIF のサイズ上限を超えています。");
        using var output = new MemoryStream();
        output.Write(original, 0, 2);
        var inserted = false;
        foreach (var segment in segments)
        {
            if (segment.Name == "EXIF")
            {
                WriteJpegExif(output, tiff);
                inserted = true;
            }
            else output.Write(original, segment.Offset, segment.Size);
        }
        if (!inserted) WriteJpegExif(output, tiff);
        // 全 scan (progressive JPEG の後続 marker を含む) をそのまま保持。
        output.Write(original, scanOffset, original.Length - scanOffset);
        return output.ToArray();
    }

    private static byte[] RewriteWebp(byte[] original, string comment)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(4, 4)) != original.Length - 8) throw InvalidImage();
        var chunks = new List<Chunk>();
        for (var offset = 12; offset < original.Length;)
        {
            if (chunks.Count >= MaxChunks) throw InvalidImage();
            RequireRange(original, offset, 8);
            var size = CheckedLength(BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(offset + 4, 4)));
            var chunk = new Chunk(offset, size, Encoding.ASCII.GetString(original, offset, 4));
            RequireRange(original, offset, checked(chunk.WebpLength));
            if ((size & 1) != 0 && original[offset + 8 + size] != 0) throw InvalidImage();
            chunks.Add(chunk);
            offset += chunk.WebpLength;
        }
        var existing = SingleExif(chunks, "EXIF");
        var oldTiff = existing == null ? null : original.AsSpan(existing.Offset + 8, existing.Size).ToArray();
        // 一部の既存 encoder は JPEG と同じ識別子を WebP EXIF に付ける。
        var prefixed = oldTiff != null && oldTiff.AsSpan().StartsWith(ExifSignature);
        if (prefixed) oldTiff = oldTiff![6..];
        var tiff = TiffUserCommentUpdater.Update(oldTiff, comment);
        if (prefixed) tiff = ExifSignature.Concat(tiff).ToArray();
        var extended = SingleExif(chunks, "VP8X");
        byte[] canvas;
        if (extended != null)
        {
            if (extended != chunks.First() || extended.Size != 10) throw InvalidImage();
            canvas = original.AsSpan(extended.Offset + 8, 10).ToArray();
            if (!chunks.Any(c => c.Name is "VP8 " or "VP8L" or "ANMF")) throw InvalidImage();
        }
        else
        {
            // Simple WebP の EXIF 追加には VP8X が必要。画素をdecodeせず寸法を読む。
            var bitstreams = chunks.Where(c => c.Name is "VP8 " or "VP8L").ToArray();
            if (bitstreams.Length != 1 || chunks.Any(c => c.Name is "ANIM" or "ANMF" or "ALPH")) throw InvalidImage();
            canvas = SimpleWebpCanvas(original, bitstreams[0]);
            if (chunks.Any(c => c.Name == "ICCP")) canvas[0] |= 0x20;
            if (chunks.Any(c => c.Name == "XMP ")) canvas[0] |= 0x04;
        }
        canvas[0] |= 0x08;
        using var output = new MemoryStream();
        output.Write(original, 0, 12);
        WriteWebpChunk(output, "VP8X", canvas);
        var inserted = false;
        foreach (var chunk in chunks)
        {
            if (chunk.Name is "VP8X" or "EXIF") continue;
            if (!inserted && chunk.Name == "XMP ")
            {
                WriteWebpChunk(output, "EXIF", tiff);
                inserted = true;
            }
            output.Write(original, chunk.Offset, chunk.WebpLength);
        }
        if (!inserted) WriteWebpChunk(output, "EXIF", tiff);
        var result = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), checked((uint)(result.Length - 8)));
        return result;
    }

    private static byte[] SimpleWebpCanvas(byte[] data, Chunk image)
    {
        var p = image.Offset + 8;
        int width, height;
        var alpha = false;
        if (image.Name == "VP8L")
        {
            if (image.Size < 5 || data[p] != 0x2f || (data[p + 4] & 0xe0) != 0) throw InvalidImage();
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 1, 4));
            width = (int)(bits & 0x3fff) + 1;
            height = (int)((bits >> 14) & 0x3fff) + 1;
            alpha = (bits & (1u << 28)) != 0;
        }
        else
        {
            if (image.Size < 10 || (data[p] & 1) != 0 || data[p + 3] != 0x9d || data[p + 4] != 1 || data[p + 5] != 0x2a)
                throw InvalidImage();
            width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 6, 2)) & 0x3fff;
            height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 8, 2)) & 0x3fff;
            if (width == 0 || height == 0) throw InvalidImage();
        }
        var canvas = new byte[10];
        if (alpha) canvas[0] |= 0x10;
        for (var i = 0; i < 3; i++)
        {
            canvas[4 + i] = (byte)((width - 1) >> (8 * i));
            canvas[7 + i] = (byte)((height - 1) >> (8 * i));
        }
        return canvas;
    }

    private static Chunk? SingleExif(List<Chunk> chunks, string name)
    {
        var matches = chunks.Where(c => c.Name == name).Take(2).ToArray();
        if (matches.Length > 1) throw InvalidImage();
        return matches.FirstOrDefault();
    }

    private static void WritePngExif(Stream output, byte[] tiff)
    {
        var chunk = new byte[tiff.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)tiff.Length);
        Encoding.ASCII.GetBytes("eXIf").CopyTo(chunk, 4);
        tiff.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), Crc(chunk.AsSpan(4, tiff.Length + 4)));
        output.Write(chunk);
    }

    private static void WriteJpegExif(Stream output, byte[] tiff)
    {
        var header = new byte[] { 0xff, 0xe1, 0, 0 };
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), (ushort)(tiff.Length + 8));
        output.Write(header);
        output.Write(ExifSignature);
        output.Write(tiff);
    }

    private static void WriteWebpChunk(Stream output, string name, byte[] payload)
    {
        var header = new byte[8];
        Encoding.ASCII.GetBytes(name).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)payload.Length);
        output.Write(header);
        output.Write(payload);
        if ((payload.Length & 1) != 0) output.WriteByte(0);
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xffffffff;
        foreach (var value in bytes)
            crc = (crc >> 8) ^ CrcTable[(int)((crc ^ value) & 0xff)];
        return ~crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (var i = 0; i < table.Length; i++)
        {
            var value = (uint)i;
            for (var bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 0 ? 0u : 0xedb88320u);
            table[i] = value;
        }
        return table;
    }

    private static bool IsName(byte[] data, int offset, string name) =>
        data.AsSpan(offset, 4).SequenceEqual(Encoding.ASCII.GetBytes(name));
    private static int CheckedLength(uint length) => length > MaxFileBytes ? throw InvalidImage() : (int)length;
    private static void RequireRange(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length) throw InvalidImage();
    }
    private static InvalidDataException InvalidImage() => new("JPEG/PNG/WebP が破損しているか、安全に編集できない構造・サイズです。");
}
