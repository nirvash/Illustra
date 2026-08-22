using System;
using System.IO;
using System.Text;
using Illustra.Helpers;
using Illustra.Tests.Helpers;
using NUnit.Framework;

namespace Illustra.Tests.Helpers
{
    /// <summary>
    /// PngTextChunkReader のテスト。
    /// ComfyUI が書き込む tEXt チャンク（"prompt" / "workflow"）を
    /// 合成 PNG で検証する。
    /// </summary>
    [TestFixture]
    public class PngTextChunkReaderTests
    {
        private string _tempFilePath;

        [SetUp]
        public void SetUp()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"illustra_pngtest_{Guid.NewGuid():N}.png");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFilePath))
                File.Delete(_tempFilePath);
        }

        [Test]
        public void TryReadTags_WithTextChunks_ReturnsTags()
        {
            // Arrange: 実際の ComfyUI 出力と同じ構造
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildPngWithTextChunks(
                ("prompt", "{\"1\":{\"inputs\":{},\"class_type\":\"KSampler\"}}"),
                ("workflow", "{\"id\":\"wf-123\",\"nodes\":[]}")
            ));

            // Act
            var result = PngTextChunkReader.TryReadTags(_tempFilePath, out var tags);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(tags, Is.Not.Null);
            Assert.That(tags.Count, Is.EqualTo(2));
            Assert.That(tags["prompt"], Is.EqualTo("{\"1\":{\"inputs\":{},\"class_type\":\"KSampler\"}}"));
            Assert.That(tags["workflow"], Is.EqualTo("{\"id\":\"wf-123\",\"nodes\":[]}"));
        }

        [Test]
        public void TryReadTags_WithJapaneseText_ReadsUtf8Correctly()
        {
            // Arrange: UTF-8 のマルチバイト文字を含む tEXt チャンク
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildPngWithTextChunks(
                ("description", "桜の花びら 🌸")
            ));

            // Act
            var result = PngTextChunkReader.TryReadTags(_tempFilePath, out var tags);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(tags["description"], Is.EqualTo("桜の花びら 🌸"));
        }

        [Test]
        public void TryReadTags_WithoutTextChunks_ReturnsFalse()
        {
            // Arrange: テキストチャンクを持たない PNG
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildPlainPng());

            // Act
            var result = PngTextChunkReader.TryReadTags(_tempFilePath, out var tags);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(tags, Is.Null);
        }

        [Test]
        public void TryReadTags_WithNonPngFile_ReturnsFalse()
        {
            // Arrange: PNG シグネチャを持たないデータ
            File.WriteAllBytes(_tempFilePath, Encoding.ASCII.GetBytes("this is not a png file at all"));

            // Act
            var result = PngTextChunkReader.TryReadTags(_tempFilePath, out var tags);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(tags, Is.Null);
        }

        [Test]
        public void TryReadTags_WithMissingFile_ReturnsFalse()
        {
            // Act
            var result = PngTextChunkReader.TryReadTags(
                Path.Combine(Path.GetTempPath(), $"not_exists_{Guid.NewGuid():N}.png"), out var tags);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(tags, Is.Null);
        }

        [Test]
        public void IsJsonLike_WithJsonString_ReturnsTrue()
        {
            Assert.That(PngTextChunkReader.IsJsonLike("{\"a\":1}"), Is.True);
            Assert.That(PngTextChunkReader.IsJsonLike("  {\"a\":1}"), Is.True);
        }

        [Test]
        public void IsJsonLike_WithNonJsonString_ReturnsFalse()
        {
            Assert.That(PngTextChunkReader.IsJsonLike("masterpiece, best quality"), Is.False);
            Assert.That(PngTextChunkReader.IsJsonLike(null), Is.False);
            Assert.That(PngTextChunkReader.IsJsonLike(""), Is.False);
            Assert.That(PngTextChunkReader.IsJsonLike("{broken"), Is.False);
        }
    }
}
