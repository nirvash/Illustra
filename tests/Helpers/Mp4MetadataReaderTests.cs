using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Illustra.Helpers;
using NUnit.Framework;

namespace Illustra.Tests.Helpers
{
    /// <summary>
    /// Mp4MetadataReader のテスト。
    /// ffmpeg が書き込む mdta 形式（moov → udta → meta → hdlr/keys/ilst）を
    /// 合成 MP4 で検証する。
    /// </summary>
    [TestFixture]
    public class Mp4MetadataReaderTests
    {
        private string _tempFilePath;

        [SetUp]
        public void SetUp()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"illustra_mp4test_{Guid.NewGuid():N}.mp4");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFilePath))
                File.Delete(_tempFilePath);
        }

        [Test]
        public void TryReadTags_WithMdtaMetadata_ReturnsTags()
        {
            // Arrange: 実際の MiniMax H3 ファイルと同じ構造
            var tags = TestMp4Builder.BuildMp4WithMetadata(new Dictionary<string, string>
            {
                ["workflow"] = "{\"id\":\"wf-123\",\"nodes\":[]}",
                ["prompt"] = "{\"92\":{\"inputs\":{\"filename_prefix\":\"video/out\"}}}"
            });
            File.WriteAllBytes(_tempFilePath, tags);

            // Act
            var result = Mp4MetadataReader.TryReadTags(_tempFilePath, out var readTags);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(readTags, Is.Not.Null);
            Assert.That(readTags.Count, Is.EqualTo(2));
            Assert.That(readTags["workflow"], Is.EqualTo("{\"id\":\"wf-123\",\"nodes\":[]}"));
            Assert.That(readTags["prompt"], Is.EqualTo("{\"92\":{\"inputs\":{\"filename_prefix\":\"video/out\"}}}"));
        }

        [Test]
        public void TryReadTags_WithJapaneseText_ReadsUtf8Correctly()
        {
            // Arrange: UTF-8 のマルチバイト文字を含むタグ
            var tags = TestMp4Builder.BuildMp4WithMetadata(new Dictionary<string, string>
            {
                ["encoder"] = "Lavf62.12.102",
                ["description"] = "花びらを追いかけて"
            });
            File.WriteAllBytes(_tempFilePath, tags);

            // Act
            var result = Mp4MetadataReader.TryReadTags(_tempFilePath, out var readTags);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(readTags["description"], Is.EqualTo("花びらを追いかけて"));
        }

        [Test]
        public void TryReadTags_WithoutMetadata_ReturnsFalse()
        {
            // Arrange: メタデータを持たない MP4 風データ
            File.WriteAllBytes(_tempFilePath, TestMp4Builder.BuildMp4WithoutMetadata());

            // Act
            var result = Mp4MetadataReader.TryReadTags(_tempFilePath, out var readTags);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(readTags, Is.Null);
        }

        [Test]
        public void TryReadTags_WithNonMp4File_ReturnsFalse()
        {
            // Arrange: MP4 構造ではないゴミデータ
            File.WriteAllBytes(_tempFilePath, Encoding.ASCII.GetBytes("this is not an mp4 file at all"));

            // Act
            var result = Mp4MetadataReader.TryReadTags(_tempFilePath, out var readTags);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(readTags, Is.Null);
        }

        [Test]
        public void TryReadTags_WithMissingFile_ReturnsFalse()
        {
            // Act
            var result = Mp4MetadataReader.TryReadTags(
                Path.Combine(Path.GetTempPath(), $"not_exists_{Guid.NewGuid():N}.mp4"), out var readTags);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(readTags, Is.Null);
        }

        [Test]
        public void TryReadTags_WithOversizedMoov_ReturnsFalse()
        {
            // Arrange: moov サイズが読み取り上限を大きく超える宣言を持つファイル。
            // 巨大アロケーションを防ぐため上限超過時はメタデータ解析対象外になる。
            // 実データは書かず SetLength で疎領域を確保してサイズ制約だけ満たす
            long declaredSize = 128L * 1024 * 1024;
            using (var fs = new FileStream(_tempFilePath, FileMode.Create, FileAccess.Write))
            {
                var header = new byte[8];
                header[0] = (byte)(declaredSize >> 24);
                header[1] = (byte)(declaredSize >> 16);
                header[2] = (byte)(declaredSize >> 8);
                header[3] = (byte)declaredSize;
                header[4] = (byte)'m';
                header[5] = (byte)'o';
                header[6] = (byte)'o';
                header[7] = (byte)'v';
                fs.Write(header, 0, header.Length);
                fs.SetLength(declaredSize);
            }

            // Act
            var result = Mp4MetadataReader.TryReadTags(_tempFilePath, out var readTags);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(readTags, Is.Null);
        }
    }
}
