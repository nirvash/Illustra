using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Illustra.Helpers;
using Illustra.Tests.Helpers;
using NUnit.Framework;

namespace Illustra.Tests.Helpers
{
    /// <summary>
    /// MediaGenerationMetadataParser（MP4 → 生成メタデータ のファサード）のテスト。
    /// Issue #50: 解析に失敗してもワークフロー埋め込み自体は表示対象になることを検証する。
    /// </summary>
    [TestFixture]
    public class MediaGenerationMetadataParserTests
    {
        private string _tempFilePath;

        [SetUp]
        public void SetUp()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"illustra_genmeta_{Guid.NewGuid():N}.mp4");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFilePath))
                File.Delete(_tempFilePath);
        }

        private static readonly string PromptJson = @"{""165"": { ""inputs"": { ""value"": ""test prompt"" }, ""class_type"": ""PrimitiveStringMultiline"" }, ""1"": { ""inputs"": { ""positive"": [""165"", 0] }, ""class_type"": ""KSampler"" }}";

        [Test]
        public void ParseFromMp4_WithComfyUITags_ReturnsMetadataWithWorkflowJson()
        {
            // Arrange
            const string workflowJson = @"{""id"":""wf-1"",""nodes"":[]}";
            File.WriteAllBytes(_tempFilePath, TestMp4Builder.BuildMp4WithMetadata(new Dictionary<string, string>
            {
                ["workflow"] = workflowJson,
                ["prompt"] = PromptJson
            }));

            // Act
            var result = MediaGenerationMetadataParser.ParseFromMp4(_tempFilePath);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Generator, Is.EqualTo("ComfyUI"));
            Assert.That(result.ParseSuccess, Is.True);
            Assert.That(result.Prompt, Is.EqualTo("test prompt"));
            Assert.That(result.HasWorkflow, Is.True);
            // 生 workflow は ComfyUI で再利用できる GUI 形式が優先される
            Assert.That(result.RawWorkflowJson, Is.EqualTo(workflowJson));
        }

        [Test]
        public void ParseFromMp4_WithUnparseablePrompt_ShowsWorkflowNotice()
        {
            // Arrange: prompt タグが存在するが JSON として解析不能でも、
            // workflow タグからのフォールバック解析が機能するケース
            File.WriteAllBytes(_tempFilePath, TestMp4Builder.BuildMp4WithMetadata(new Dictionary<string, string>
            {
                ["prompt"] = "this is not a json string",
                ["workflow"] = @"{""id"":""wf-2""}"
            }));

            // Act
            var result = MediaGenerationMetadataParser.ParseFromMp4(_tempFilePath);

            // Assert: 解析失敗でもワークフロー埋め込みとして表示対象になる
            Assert.That(result, Is.Not.Null);
            Assert.That(result.HasWorkflow, Is.True);
            Assert.That(result.NeedsWorkflowNotice, Is.True);
        }

        [Test]
        public void ParseFromMp4_WithoutTags_ReturnsNull()
        {
            // Arrange
            File.WriteAllBytes(_tempFilePath, TestMp4Builder.BuildMp4WithoutMetadata());

            // Act
            var result = MediaGenerationMetadataParser.ParseFromMp4(_tempFilePath);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ParseFromMp4_WithNonMp4File_ReturnsNull()
        {
            // Arrange
            File.WriteAllBytes(_tempFilePath, Encoding.ASCII.GetBytes("garbage data"));

            // Act
            var result = MediaGenerationMetadataParser.ParseFromMp4(_tempFilePath);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ParseFromPng_WithComfyUiChunks_ReturnsMetadata()
        {
            // Arrange: ComfyUI が出力する PNG と同じ構造
            const string workflowJson = @"{""id"":""wf-1"",""nodes"":[]}";
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildComfyUiPng(PromptJson, workflowJson));

            // Act
            var result = MediaGenerationMetadataParser.ParseFromPng(_tempFilePath);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Generator, Is.EqualTo("ComfyUI"));
            Assert.That(result.ParseSuccess, Is.True);
            Assert.That(result.Prompt, Is.EqualTo("test prompt"));
            Assert.That(result.HasWorkflow, Is.True);
            Assert.That(result.RawWorkflowJson, Is.EqualTo(workflowJson));
        }

        [Test]
        public void ParseFromPng_WithPlainPng_ReturnsNull()
        {
            // Arrange: テキストチャンクなしの PNG（ComfyUI 以外）
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildPlainPng());

            // Act
            var result = MediaGenerationMetadataParser.ParseFromPng(_tempFilePath);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ParseFromPng_WithNonJsonTextChunks_ReturnsNull()
        {
            // Arrange: prompt / workflow 以外のテキストチャンクのみの PNG
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildPngWithTextChunks(
                ("Software", "Some Image Editor"),
                ("Comment", "masterpiece, best quality")
            ));

            // Act
            var result = MediaGenerationMetadataParser.ParseFromPng(_tempFilePath);

            // Assert: ComfyUI 埋め込みではないため null
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ParseFromPng_WithUnparseablePrompt_FallsBackToWorkflow()
        {
            // Arrange: prompt が解析不能でも workflow チャンクは有効なケース（未知ノードのみ）
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildComfyUiPng(
                @"{""999"": {""inputs"": {}, ""class_type"": ""UnknownNode""}}",
                @"{""id"":""wf-3"",""nodes"":[]}"));

            // Act
            var result = MediaGenerationMetadataParser.ParseFromPng(_tempFilePath);

            // Assert: 解析失敗でもワークフロー埋め込みとして表示対象になる
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Generator, Is.EqualTo("ComfyUI"));
            Assert.That(result.HasWorkflow, Is.True);
            Assert.That(result.NeedsWorkflowNotice, Is.True);
        }

        [Test]
        public void ParseFromPng_WithNonPngFile_ReturnsNull()
        {
            // Arrange
            File.WriteAllBytes(_tempFilePath, Encoding.ASCII.GetBytes("not a png"));

            // Act
            var result = MediaGenerationMetadataParser.ParseFromPng(_tempFilePath);

            // Assert
            Assert.That(result, Is.Null);
        }
    }
}
