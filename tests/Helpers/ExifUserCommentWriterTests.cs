using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Illustra.Helpers;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using NUnit.Framework;
using SkiaSharp;

namespace Illustra.Tests.Helpers;

[TestFixture]
public class ExifUserCommentWriterTests
{
    private const string Comment = "日本語の説明 🌱 😀";

    [Test]
    public void CommitPrepared_CompetingOldSnapshotsCannotOverwriteFirstCommit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"illustra-exif-race-{Guid.NewGuid():N}.png");
        var firstTemp = path + ".first.tmp";
        var secondTemp = path + ".second.tmp";
        var original = Encode(SKEncodedImageFormat.Png);
        var first = ExifUserCommentWriter.Rewrite(original, "先行保存 🌱");
        var second = ExifUserCommentWriter.Rewrite(original, "後続保存 🌼");
        using var secondCompared = new ManualResetEvent(false);
        using var secondFinished = new ManualResetEvent(false);
        using var firstReplaced = new ManualResetEvent(false);
        Exception? secondError = null;
        Thread? contender = null;
        try
        {
            File.WriteAllBytes(path, original);
            File.WriteAllBytes(firstTemp, first);
            File.WriteAllBytes(secondTemp, second);
            Commit(path, original, firstTemp, (source, destination) =>
            {
                // 先行保存が比較を終えた直後、同じ旧snapshotの保存を別threadから試す。
                contender = new Thread(() =>
                {
                    try
                    {
                        Commit(path, original, secondTemp, (laterSource, laterDestination) =>
                        {
                            secondCompared.Set();
                            if (!firstReplaced.WaitOne(5000)) throw new TimeoutException("先行保存の完了待ち");
                            ReplaceSynthetic(laterSource, laterDestination);
                        }, 0);
                    }
                    catch (Exception ex) { secondError = ex; }
                    finally { secondFinished.Set(); }
                }) { IsBackground = true };
                contender.Start();
                // 旧実装なら後続が比較を完了。修正後は待機時間0でMutex取得を拒否。
                Assert.That(WaitHandle.WaitAny(new WaitHandle[] { secondCompared, secondFinished }, 5000), Is.Not.EqualTo(WaitHandle.WaitTimeout));
                ReplaceSynthetic(source, destination);
                firstReplaced.Set();
            });
            Assert.That(contender!.Join(5000), Is.True);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(first), "先行保存が後続の旧snapshotで失われないこと");
            Assert.That(secondError, Is.InstanceOf<TimeoutException>());
            Assert.That(File.Exists(firstTemp) || File.Exists(secondTemp), Is.False);
            // 先行commit完了後に再試行しても、旧snapshotからの出力は比較で拒否される。
            File.WriteAllBytes(secondTemp, second);
            Assert.Throws<IOException>(() => Commit(path, original, secondTemp, ReplaceSynthetic));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(first));
            Assert.That(File.Exists(secondTemp), Is.False);
        }
        finally
        {
            firstReplaced.Set();
            contender?.Join(5000);
            File.Delete(firstTemp);
            File.Delete(secondTemp);
            File.Delete(path);
        }
    }

    [Test]
    public void CommitPrepared_WaitingOldSnapshotIsRejectedAfterFirstCommit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"illustra-exif-waiting-{Guid.NewGuid():N}.png");
        var firstTemp = path + ".first.tmp";
        var secondTemp = path + ".second.tmp";
        var original = Encode(SKEncodedImageFormat.Png);
        var first = ExifUserCommentWriter.Rewrite(original, "先行保存 🌱");
        var second = ExifUserCommentWriter.Rewrite(original, "後続保存 🌼");
        using var firstReplaced = new ManualResetEvent(false);
        Exception? secondError = null;
        Thread? contender = null;
        try
        {
            File.WriteAllBytes(path, original);
            File.WriteAllBytes(firstTemp, first);
            File.WriteAllBytes(secondTemp, second);
            Commit(path, original, firstTemp, (source, destination) =>
            {
                contender = new Thread(() =>
                {
                    try
                    {
                        Commit(path, original, secondTemp, (laterSource, laterDestination) =>
                        {
                            if (!firstReplaced.WaitOne(5000)) throw new TimeoutException("先行保存の完了待ち");
                            ReplaceSynthetic(laterSource, laterDestination);
                        });
                    }
                    catch (Exception ex) { secondError = ex; }
                }) { IsBackground = true };
                contender.Start();
                ReplaceSynthetic(source, destination);
                firstReplaced.Set();
            });
            Assert.That(contender!.Join(5000), Is.True);
            Assert.That(secondError, Is.InstanceOf<IOException>());
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(first));
            Assert.That(File.Exists(firstTemp) || File.Exists(secondTemp), Is.False);
        }
        finally
        {
            firstReplaced.Set();
            contender?.Join(5000);
            File.Delete(firstTemp); File.Delete(secondTemp); File.Delete(path);
        }
    }

    [Test]
    public void CommitPrepared_RejectsStaleSnapshotAndCleansTemporaryFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"illustra-exif-stale-{Guid.NewGuid():N}.png");
        var temp = path + ".prepared.tmp";
        var original = ExifUserCommentWriter.Rewrite(Encode(SKEncodedImageFormat.Png), "元の説明");
        var changed = (byte[])original.Clone();
        changed[changed.Length / 2] ^= 1; // 同じ長さでも全byte比較で検出する。
        try
        {
            File.WriteAllBytes(path, changed);
            File.WriteAllBytes(temp, ExifUserCommentWriter.Rewrite(original, Comment));
            Assert.Throws<IOException>(() => Commit(path, original, temp, ReplaceSynthetic));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(changed));
            Assert.That(File.Exists(temp), Is.False);
        }
        finally { File.Delete(temp); File.Delete(path); }
    }

    private static void Commit(string path, byte[] original, string temp,
        Action<string, string>? replace = null, int timeout = 10000)
    {
        try
        {
            typeof(ExifUserCommentWriter).GetMethod("CommitPrepared", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object?[] { path, original, temp, replace, timeout });
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    // 同期競合だけを検証する代替。製品コードのFile.Replaceの権限や設定は変えない。
    private static void ReplaceSynthetic(string source, string destination)
    {
        File.WriteAllBytes(destination, File.ReadAllBytes(source));
        File.Delete(source);
    }

    [Test]
    public void CommitPrepared_DifferentPathsCommitIndependently()
    {
        var firstPath = Path.Combine(Path.GetTempPath(), $"illustra-exif-independent-{Guid.NewGuid():N}.png");
        var secondPath = firstPath + ".other.png";
        var firstTemp = firstPath + ".prepared.tmp";
        var secondTemp = secondPath + ".prepared.tmp";
        var original = Encode(SKEncodedImageFormat.Png);
        var updated = ExifUserCommentWriter.Rewrite(original, Comment);
        Exception? secondError = null;
        try
        {
            File.WriteAllBytes(firstPath, original);
            File.WriteAllBytes(secondPath, original);
            File.WriteAllBytes(firstTemp, updated);
            File.WriteAllBytes(secondTemp, updated);
            Commit(firstPath, original, firstTemp, (source, destination) =>
            {
                var other = new Thread(() =>
                {
                    try { Commit(secondPath, original, secondTemp, ReplaceSynthetic, 0); }
                    catch (Exception ex) { secondError = ex; }
                }) { IsBackground = true };
                other.Start();
                Assert.That(other.Join(5000), Is.True);
                Assert.That(secondError, Is.Null, "別pathは先行commitのMutexに阻まれないこと");
                ReplaceSynthetic(source, destination);
            });
            Assert.That(File.ReadAllBytes(firstPath), Is.EqualTo(updated));
            Assert.That(File.ReadAllBytes(secondPath), Is.EqualTo(updated));
        }
        finally
        {
            File.Delete(firstTemp); File.Delete(secondTemp);
            File.Delete(firstPath); File.Delete(secondPath);
        }
    }

    [Test]
    public void CommitMutexName_NormalizesCaseSeparatorsAndRelativeComponents()
    {
        var path = Path.Combine(Path.GetTempPath(), $"illustra-exif-name-{Guid.NewGuid():N}.png");
        var alias = Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path).ToUpperInvariant()).Replace('\\', '/');
        Assert.That(MutexName(alias), Is.EqualTo(MutexName(path)));
        Assert.That(MutexName(path + ".other"), Is.Not.EqualTo(MutexName(path)));
        Assert.That(MutexName(path), Does.StartWith(@"Global\Illustra.ExifUserComment."));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CommitPrepared_AbandonedMutexRevalidatesSnapshotAndReleasesOwnership(bool stale)
    {
        var path = Path.Combine(Path.GetTempPath(), $"illustra-exif-abandoned-{Guid.NewGuid():N}.png");
        var temp = path + ".prepared.tmp";
        var original = Encode(SKEncodedImageFormat.Png);
        var updated = ExifUserCommentWriter.Rewrite(original, Comment);
        using var abandoned = new Mutex(false, MutexName(path));
        // handleを保持して、thread終了後も同名Mutexのabandoned状態を残す。
        var owner = new Thread(() => abandoned.WaitOne()) { IsBackground = true };
        try
        {
            File.WriteAllBytes(path, stale ? updated : original);
            File.WriteAllBytes(temp, updated);
            owner.Start();
            Assert.That(owner.Join(5000), Is.True);
            if (stale) Assert.Throws<IOException>(() => Commit(path, original, temp, ReplaceSynthetic));
            else Commit(path, original, temp, ReplaceSynthetic);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(updated));
            Assert.That(File.Exists(temp), Is.False);
            // 成功/比較拒否のどちらでもMutexを解放し、別threadから再取得できること。
            Exception? retryError = null;
            File.WriteAllBytes(temp, updated);
            var retry = new Thread(() =>
            {
                try { Commit(path, updated, temp, ReplaceSynthetic, 0); }
                catch (Exception ex) { retryError = ex; }
            }) { IsBackground = true };
            retry.Start();
            Assert.That(retry.Join(5000), Is.True);
            Assert.That(retryError, Is.Null);
        }
        finally { owner.Join(5000); File.Delete(temp); File.Delete(path); }
    }

    [Test]
    public void CommitPrepared_ReplacementFailureReleasesMutexAndCleansTemporaryFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"illustra-exif-failed-{Guid.NewGuid():N}.png");
        var temp = path + ".prepared.tmp";
        var original = Encode(SKEncodedImageFormat.Png);
        var updated = ExifUserCommentWriter.Rewrite(original, Comment);
        try
        {
            File.WriteAllBytes(path, original);
            File.WriteAllBytes(temp, updated);
            Assert.Throws<IOException>(() => Commit(path, original, temp, (_, _) => throw new IOException("置換失敗fixture")));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            Assert.That(File.Exists(temp), Is.False);
            File.WriteAllBytes(temp, updated);
            Exception? retryError = null;
            var retry = new Thread(() =>
            {
                try { Commit(path, original, temp, ReplaceSynthetic, 0); }
                catch (Exception ex) { retryError = ex; }
            }) { IsBackground = true };
            retry.Start();
            Assert.That(retry.Join(5000), Is.True);
            Assert.That(retryError, Is.Null);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(updated));
        }
        finally { File.Delete(temp); File.Delete(path); }
    }

    private static string MutexName(string path) => (string)typeof(ExifUserCommentWriter)
        .GetMethod("GetCommitMutexName", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { path })!;

    [TestCase(SKEncodedImageFormat.Jpeg)]
    [TestCase(SKEncodedImageFormat.Png)]
    [TestCase(SKEncodedImageFormat.Webp)]
    public void Rewrite_RoundTripsUtf16BeAndPreservesExifAndImageData(SKEncodedImageFormat format)
    {
        var original = Encode(format);
        var saved = ExifUserCommentWriter.Rewrite(original, Comment);
        AssertComment(saved, Comment);
        var withArtist = AddArtist(saved);
        var updated = ExifUserCommentWriter.Rewrite(withArtist, "更新 🌼");
        AssertComment(updated, "更新 🌼");
        var ifd = ImageMetadataReader.ReadMetadata(new MemoryStream(updated)).OfType<ExifIfd0Directory>().Single();
        Assert.That(ifd.GetString(ExifDirectoryBase.TagArtist), Is.EqualTo("fixture"));
        Assert.That(ImagePayload(updated, format), Is.EqualTo(ImagePayload(original, format)));
        using var decoded = SKBitmap.Decode(updated);
        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded.Width, Is.EqualTo(2));
    }

    [Test]
    public void Rewrite_PreservesPngTextAndApngChunks()
    {
        var input = TestPngBuilder.BuildTwoFrameApng("合成 fixture");
        var output = ExifUserCommentWriter.Rewrite(input, Comment);
        AssertComment(output, Comment);
        foreach (var name in new[] { "tEXt", "acTL", "fcTL", "IDAT", "fdAT" })
            Assert.That(PngChunks(output, name), Is.EqualTo(PngChunks(input, name)));
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(PngChunks(output, "acTL").AsSpan(8)), Is.EqualTo(2));
    }

    [TestCase(SKEncodedImageFormat.Jpeg)]
    [TestCase(SKEncodedImageFormat.Png)]
    [TestCase(SKEncodedImageFormat.Webp)]
    public void Rewrite_PreservesLittleEndianTiffAndUtf16Be(SKEncodedImageFormat format)
    {
        var initial = ExifUserCommentWriter.Rewrite(Encode(format), Comment);
        var tiff = new byte[34];
        tiff[0] = tiff[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(10), 0x013b);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(14), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(18), 26);
        Encoding.ASCII.GetBytes("fixture\0").CopyTo(tiff, 26);
        var input = ReplaceTiff(initial, FindTiff(initial), tiff);
        var output = ExifUserCommentWriter.Rewrite(input, Comment);
        AssertComment(output, Comment);
        var exif = ImageMetadataReader.ReadMetadata(new MemoryStream(output)).OfType<ExifIfd0Directory>().Single();
        Assert.That(exif.GetString(ExifDirectoryBase.TagArtist), Is.EqualTo("fixture"));
        // 元の IFD/値の絶対位置を保持。変更するのはヘッダの root pointer のみ。
        var actualTiff = output.AsSpan(FindLittleEndianTiff(output), tiff.Length).ToArray();
        Assert.That(actualTiff[..4], Is.EqualTo(tiff[..4]));
        Assert.That(actualTiff[8..], Is.EqualTo(tiff[8..]));
    }

    [Test]
    public void Rewrite_PreservesAnimatedWebpAndXmp()
    {
        var still = Encode(SKEncodedImageFormat.Webp);
        var image = WebpChunks(still, "VP8 ").Concat(WebpChunks(still, "VP8L")).ToArray();
        var frame = new byte[16].Concat(image).ToArray();
        frame[6] = 1;
        frame[9] = 1;
        frame[12] = 10;
        var canvas = new byte[10];
        canvas[0] = 0x06; // animation + XMP
        canvas[4] = 1;
        canvas[7] = 1;
        var input = Webp(WebpChunk("VP8X", canvas), WebpChunk("ANIM", new byte[6]),
            WebpChunk("ANMF", frame), WebpChunk("ANMF", frame), WebpChunk("XMP ", Encoding.UTF8.GetBytes("<fixture/>")));
        var output = ExifUserCommentWriter.Rewrite(input, Comment);
        AssertComment(output, Comment);
        foreach (var name in new[] { "ANIM", "ANMF", "XMP " })
            Assert.That(WebpChunks(output, name), Is.EqualTo(WebpChunks(input, name)));
        using var codec = SKCodec.Create(new SKMemoryStream(output));
        Assert.That(codec, Is.Not.Null);
        Assert.That(codec.FrameCount, Is.EqualTo(2));
    }

    [TestCase(SKEncodedImageFormat.Jpeg)]
    [TestCase(SKEncodedImageFormat.Png)]
    [TestCase(SKEncodedImageFormat.Webp)]
    public async Task Save_ReplacesOnlyAfterReadAsync(SKEncodedImageFormat format)
    {
        if (!CanReplaceFiles()) Assert.Ignore("sandbox の ACL で厳密な File.Replace が許可されないため、成功保存は sandbox 外で検証する。");
        var path = Path.Combine(Path.GetTempPath(), $"illustra-exif-{Guid.NewGuid():N}.fixture");
        try
        {
            await File.WriteAllBytesAsync(path, Encode(format));
            await ExifUserCommentWriter.SaveAsync(path, Comment);
            AssertComment(await File.ReadAllBytesAsync(path), Comment);
            var properties = new Illustra.Models.ImagePropertiesModel();
            typeof(Illustra.Models.ImagePropertiesModel).GetMethod("ReadExifData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object[] { path, properties });
            Assert.That(properties.UserComment, Is.EqualTo(Comment), "表示モデルにもBOMなしで戻ること");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase(SKEncodedImageFormat.Jpeg)]
    [TestCase(SKEncodedImageFormat.Png)]
    [TestCase(SKEncodedImageFormat.Webp)]
    public async Task Save_LeavesOriginalAndNoTemporaryFileOnMalformedExifAsync(SKEncodedImageFormat format)
    {
        var path = Path.Combine(Path.GetTempPath(), $"illustra-exif-{Guid.NewGuid():N}.fixture");
        try
        {
            var broken = ExifUserCommentWriter.Rewrite(Encode(format), Comment);
            var tiff = FindTiff(broken);
            // IFD0 points outside the TIFF payload.
            broken[tiff + 4] = broken[tiff + 5] = broken[tiff + 6] = broken[tiff + 7] = 0xff;
            if (format == SKEncodedImageFormat.Png) FixExifPngCrc(broken);
            await File.WriteAllBytesAsync(path, broken);
            Assert.ThrowsAsync<InvalidDataException>(() => ExifUserCommentWriter.SaveAsync(path, "failure"));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(broken));
            Assert.That(System.IO.Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"), Is.Empty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Save_LeavesOriginalAndNoTemporaryFileWhenOriginalIsReadOnlyAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"illustra-exif-{Guid.NewGuid():N}.fixture");
        var original = Encode(SKEncodedImageFormat.Png);
        try
        {
            await File.WriteAllBytesAsync(path, original);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            Assert.ThrowsAsync<UnauthorizedAccessException>(() => ExifUserCommentWriter.SaveAsync(path, Comment));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
            Assert.That(System.IO.Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"), Is.Empty);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
    }

    [TestCase("cycle")]
    [TestCase("type")]
    [TestCase("truncated")]
    [TestCase("value")]
    public void Rewrite_RejectsMalformedTiff(string kind)
    {
        var png = ExifUserCommentWriter.Rewrite(Encode(SKEncodedImageFormat.Png), Comment);
        var offset = FindTiff(png);
        var tiff = png.AsSpan(offset, TiffLength(png, offset)).ToArray();
        var root = (int)BinaryPrimitives.ReadUInt32BigEndian(tiff.AsSpan(4));
        if (kind == "cycle") BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(root + 10), (uint)root);
        if (kind == "type") BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(root + 4), 0xffff);
        if (kind == "truncated") tiff = tiff[..^5];
        if (kind == "value") BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(root + 10), uint.MaxValue);
        var input = ReplaceTiff(png, offset, tiff);
        Assert.Throws<InvalidDataException>(() => ExifUserCommentWriter.Rewrite(input, Comment));
    }

    [Test]
    public void Rewrite_RejectsNonImagesTruncationAndOversizedComments()
    {
        Assert.Throws<InvalidDataException>(() => ExifUserCommentWriter.Rewrite(Encoding.UTF8.GetBytes("text"), Comment));
        foreach (var format in new[] { SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp })
        {
            var image = Encode(format);
            Assert.Throws<InvalidDataException>(() => ExifUserCommentWriter.Rewrite(image[..^4], Comment));
        }
        Assert.Throws<ArgumentException>(() => ExifUserCommentWriter.Rewrite(Encode(SKEncodedImageFormat.Png), new string('a', 32768)));
    }

    private static byte[] Encode(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(new SKColor(20, 120, 220));
        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    private static void AssertComment(byte[] image, string expected)
    {
        var exif = ImageMetadataReader.ReadMetadata(new MemoryStream(image)).OfType<ExifSubIfdDirectory>().Single();
        var bytes = exif.GetByteArray(ExifDirectoryBase.TagUserComment)!;
        Assert.That(bytes[..8], Is.EqualTo(Encoding.ASCII.GetBytes("UNICODE\0")));
        Assert.That(bytes[8..10], Is.EqualTo(new byte[] { 0xfe, 0xff }));
        Assert.That(Encoding.BigEndianUnicode.GetString(bytes[10..]), Is.EqualTo(expected));
        Assert.That(StableDiffusionTools.StableDiffusionMetadataManager.DecodeUtf16(bytes[8..]), Is.EqualTo(expected));
        Assert.That(exif.GetDescription(ExifDirectoryBase.TagUserComment)?.TrimStart('\uFEFF'), Is.EqualTo(expected));
    }

    private static byte[] AddArtist(byte[] image)
    {
        // Writer creates a big-endian TIFF. Add an inline ASCII Artist entry to
        // the relocated IFD0 without moving existing EXIF values.
        var tiffOffset = FindTiff(image);
        var root = checked((int)BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(tiffOffset + 4)));
        var count = BinaryPrimitives.ReadUInt16BigEndian(image.AsSpan(tiffOffset + root));
        var entries = image.AsSpan(tiffOffset + root + 2, count * 12).ToArray();
        var newTiff = image.AsSpan(tiffOffset, TiffLength(image, tiffOffset)).ToArray().ToList();
        var artistOffset = newTiff.Count;
        newTiff.AddRange(Encoding.ASCII.GetBytes("fixture\0"));
        var newRoot = newTiff.Count;
        var ifd = new byte[2 + (count + 1) * 12 + 4];
        BinaryPrimitives.WriteUInt16BigEndian(ifd, (ushort)(count + 1));
        BinaryPrimitives.WriteUInt16BigEndian(ifd.AsSpan(2), 0x013b);
        BinaryPrimitives.WriteUInt16BigEndian(ifd.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32BigEndian(ifd.AsSpan(6), 8);
        BinaryPrimitives.WriteUInt32BigEndian(ifd.AsSpan(10), (uint)artistOffset);
        entries.CopyTo(ifd, 14);
        newTiff.AddRange(ifd);
        var tiff = newTiff.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(4), (uint)newRoot);
        return ReplaceTiff(image, tiffOffset, tiff);
    }

    private static int FindTiff(byte[] data)
    {
        for (var i = 0; i < data.Length - 4; i++)
            if (data[i] == 'M' && data[i + 1] == 'M' && data[i + 2] == 0 && data[i + 3] == 42) return i;
        throw new InvalidDataException();
    }

    private static int FindLittleEndianTiff(byte[] data)
    {
        for (var i = 0; i < data.Length - 4; i++)
            if (data[i] == 'I' && data[i + 1] == 'I' && data[i + 2] == 42 && data[i + 3] == 0) return i;
        throw new InvalidDataException();
    }

    private static bool CanReplaceFiles()
    {
        var source = Path.Combine(Path.GetTempPath(), $"illustra-exif-capability-{Guid.NewGuid():N}.tmp");
        var target = source + ".target";
        try
        {
            File.WriteAllText(source, "new");
            File.WriteAllText(target, "old");
            try { File.Replace(source, target, null); return true; }
            catch (UnauthorizedAccessException) { return false; }
        }
        finally { File.Delete(source); File.Delete(target); }
    }

    private static int TiffLength(byte[] data, int offset) => data[0] == 0xff
        ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset - 8)) - 8
        : data[0] == 137 ? (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset - 8))
        : (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset - 4));

    private static byte[] ReplaceTiff(byte[] image, int offset, byte[] tiff)
    {
        var oldLength = TiffLength(image, offset);
        if (image[0] == 0xff)
        {
            var output = image[..offset].Concat(tiff).Concat(image[(offset + oldLength)..]).ToArray();
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(offset - 8), (ushort)(tiff.Length + 8));
            return output;
        }
        if (image[0] == 137)
            return image[..(offset - 8)].Concat(PngChunk("eXIf", tiff)).Concat(image[(offset + oldLength + 4)..]).ToArray();
        var result = image[..(offset - 8)].Concat(WebpChunk("EXIF", tiff)).Concat(image[(offset + oldLength + (oldLength & 1))..]).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(result.Length - 8));
        return result;
    }

    private static byte[] ImagePayload(byte[] data, SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Png => PngChunks(data, "IDAT"),
        SKEncodedImageFormat.Webp => WebpChunks(data, "VP8 ").Concat(WebpChunks(data, "VP8L")).ToArray(),
        _ => JpegScan(data)
    };

    private static byte[] JpegScan(byte[] data)
    {
        var p = 2;
        while (data[p + 1] != 0xda) p += 2 + BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p + 2));
        return data[p..];
    }

    private static byte[] PngChunks(byte[] data, string name)
    {
        using var result = new MemoryStream();
        for (var p = 8; p < data.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p)) + 12);
            if (Encoding.ASCII.GetString(data, p + 4, 4) == name) result.Write(data, p, length);
            p += length;
        }
        return result.ToArray();
    }

    private static byte[] WebpChunks(byte[] data, string name)
    {
        using var result = new MemoryStream();
        for (var p = 12; p < data.Length;)
        {
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 4));
            var length = size + 8 + (size & 1);
            if (Encoding.ASCII.GetString(data, p, 4) == name) result.Write(data, p, length);
            p += length;
        }
        return result.ToArray();
    }

    private static byte[] PngChunk(string name, byte[] payload)
    {
        var result = new byte[payload.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)payload.Length);
        Encoding.ASCII.GetBytes(name).CopyTo(result, 4);
        payload.CopyTo(result, 8);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(result.Length - 4), Crc(result.AsSpan(4, payload.Length + 4)));
        return result;
    }

    private static void FixExifPngCrc(byte[] data)
    {
        var tiff = FindTiff(data);
        var length = TiffLength(data, tiff);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(tiff + length), Crc(data.AsSpan(tiff - 4, length + 4)));
    }

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xffffffff;
        foreach (var value in data)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        return ~crc;
    }

    private static byte[] WebpChunk(string name, byte[] payload)
    {
        var result = new byte[8 + payload.Length + (payload.Length & 1)];
        Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(result, 8);
        return result;
    }

    private static byte[] Webp(params byte[][] chunks)
    {
        var result = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBP").Concat(chunks.SelectMany(x => x)).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(result.Length - 8));
        return result;
    }
}
