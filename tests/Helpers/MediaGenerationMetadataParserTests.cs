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
            // Arrange: prompt タグが解析不能でも workflow タグは有効なケース
            File.WriteAllBytes(_tempFilePath, TestMp4Builder.BuildMp4WithMetadata(new Dictionary<string, string>
            {
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
    }
}
