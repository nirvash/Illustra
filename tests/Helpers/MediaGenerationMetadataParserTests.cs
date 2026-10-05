using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Reflection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
        public void ParseFromMp4_WithH3ContextLoopPlan_ReturnsShotPrompts()
        {
            const string workflowJson = @"{""id"":""h3-chain"",""nodes"":[]}";
            var plan = new JsonObject
            {
                ["shots"] = new JsonArray("first scene", "second scene")
            };
            var graph = new JsonObject
            {
                ["1"] = new JsonObject
                {
                    ["inputs"] = new JsonObject { ["plan_json"] = plan.ToJsonString() },
                    ["class_type"] = "MiniMaxH3ChainPlan"
                }
            };
            File.WriteAllBytes(_tempFilePath, TestMp4Builder.BuildMp4WithMetadata(
                new Dictionary<string, string>
                {
                    ["workflow"] = workflowJson,
                    ["prompt"] = graph.ToJsonString()
                }));

            var result = MediaGenerationMetadataParser.ParseFromMp4(_tempFilePath);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.ParseSuccess, Is.True);
            Assert.That(result.Prompt, Is.EqualTo("first scene\n\nsecond scene"));
            Assert.That(result.HasWorkflow, Is.True);
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

        [Test]
        public void ParseFromPng_WithNovelAiTextMetadata_MapsPromptParametersAndRawMetadata()
        {
            const string comment = "{\"steps\":50,\"sampler\":\"k_euler_ancestral\",\"seed\":2253955223,\"scale\":10.0,\"strength\":0.7,\"noise\":0.2,\"uc\":\"bad anatomy\"}";
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildPngWithTextChunks(
                ("Description", "a cat in space"), ("Software", "NovelAI"),
                ("Source", "Stable Diffusion model"), ("Comment", comment)));

            var result = MediaGenerationMetadataParser.ParseFromPng(_tempFilePath);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Generator, Is.EqualTo("NovelAI"));
            Assert.That(result.ModelName, Is.EqualTo("Stable Diffusion model"));
            Assert.That(result.Prompt, Is.EqualTo("a cat in space"));
            Assert.That(result.NegativePrompt, Is.EqualTo("bad anatomy"));
            Assert.That(result.Parameters["steps"], Is.EqualTo("50"));
            Assert.That(result.Parameters["sampler"], Is.EqualTo("k_euler_ancestral"));
            Assert.That(result.Parameters["seed"], Is.EqualTo("2253955223"));
            Assert.That(result.Parameters["scale"], Is.EqualTo("10.0"));
            Assert.That(result.Parameters.Keys, Is.EquivalentTo(new[] { "steps", "sampler", "seed", "scale" }));
            Assert.That(result.Parameters.ContainsKey("strength"), Is.False);
            Assert.That(result.Parameters.ContainsKey("noise"), Is.False);
            Assert.That(result.Parameters.ContainsKey("Software"), Is.False);
            Assert.That(result.Parameters.ContainsKey("Source"), Is.False);
            Assert.That(result.RawMetadata, Does.Contain(comment));
            Assert.That(result.RawMetadata, Does.Contain("Description: a cat in space"));
            Assert.That(result.RawMetadata, Does.Contain("Software: NovelAI"));
            Assert.That(result.RawMetadata, Does.Contain("Source: Stable Diffusion model"));
        }

        [Test]
        public void NovelAiStealthDecoder_OnAnimatedPng_LoadsOnlyFirstFrame()
        {
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildTwoFrameApng("first frame only"));
            MethodInfo decoder = typeof(NovelAIMetadataParser).GetMethod("LoadFirstFrame",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(decoder, Is.Not.Null, "the stealth decoder must use a frame-limited ImageSharp path");

            using var image = (Image<Rgba32>)decoder!.Invoke(null, new object[] { _tempFilePath })!;
            Assert.That(image.Frames.Count, Is.EqualTo(1));
        }

        [Test]
        public void ParseFromPng_WithNovelAiCommentPromptAndWithoutDescription_UsesCommentPrompt()
        {
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildPngWithTextChunks(
                ("Software", "NovelAI"),
                ("Comment", "{\"prompt\":\"one character\",\"uc\":\"blur\",\"steps\":28}")));

            var result = MediaGenerationMetadataParser.ParseFromPng(_tempFilePath);

            Assert.That(result.Prompt, Is.EqualTo("one character"));
            Assert.That(result.NegativePrompt, Is.EqualTo("blur"));
        }

        [TestCase("tEXt")]
        [TestCase("iTXt")]
        [TestCase("zTXt")]
        public void ParseFromPng_WithNovelAiDescriptionChunkTypes_ParsesText(string type)
        {
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildPngWithTextChunk(
                type, "Description", "a peaceful landscape", ("Software", "NovelAI")));

            var result = MediaGenerationMetadataParser.ParseFromPng(_tempFilePath);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Prompt, Is.EqualTo("a peaceful landscape"));
        }

        [Test]
        public void ParseFromPng_WithNonNovelAiJsonComment_DoesNotMisidentify()
        {
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildPngWithTextChunks(
                ("Description", "ordinary image text"), ("Comment", "{\"steps\":20,\"prompt\":\"other tool\"}")));
            Assert.That(MediaGenerationMetadataParser.ParseFromPng(_tempFilePath), Is.Null);
        }

        [Test]
        public void ParseFromPng_WithAlphaStealthMetadata_ParsesAndPreservesV4Structure()
        {
            const string comment = "{\"base_caption\":{\"caption\":\"two people\"},\"char_captions\":[{\"char_caption\":\"red coat\",\"centers\":[[0.2,0.3]]}],\"strength\":0.7,\"noise\":0.2,\"uc\":\"low quality\",\"steps\":24}";
            string stealthJson = "{\"Software\":\"NovelAI\",\"Description\":\"fallback\",\"Comment\":" + System.Text.Json.JsonSerializer.Serialize(comment) + "}";
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildStealthPng(stealthJson));

            var result = MediaGenerationMetadataParser.ParseFromPng(_tempFilePath);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Prompt, Is.EqualTo("fallback"));
            Assert.That(result.NegativePrompt, Is.EqualTo("low quality"));
            Assert.That(result.Parameters.Keys, Is.EquivalentTo(new[] { "steps" }));
            Assert.That(result.Parameters.ContainsKey("base_caption"), Is.False);
            Assert.That(result.Parameters.ContainsKey("char_captions"), Is.False);
            Assert.That(result.Parameters.ContainsKey("strength"), Is.False);
            Assert.That(result.Parameters.ContainsKey("noise"), Is.False);
            Assert.That(result.RawMetadata, Does.Contain("char_captions"));
            Assert.That(result.RawMetadata, Does.Contain("two people"));
            Assert.That(result.RawMetadata, Does.Contain("strength"));
            Assert.That(result.RawMetadata, Does.Contain("noise"));
            Assert.That(result.RawMetadata, Is.EqualTo(stealthJson));
        }

        [Test]
        public void ParseFromPng_TextNovelAiMetadataTakesPriorityOverAlphaFallback()
        {
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildStealthPng(
                "{\"Software\":\"NovelAI\",\"Description\":\"alpha prompt\"}", "text prompt", true));
            var result = MediaGenerationMetadataParser.ParseFromPng(_tempFilePath);
            Assert.That(result.Prompt, Is.EqualTo("text prompt"));
        }

        [TestCase("[]")]
        [TestCase("{\"unrelated\":\"value\"}")]
        public void ParseFromPng_WithInvalidStealthValue_FailsSafely(string json)
        {
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildStealthPng(json));
            Assert.That(MediaGenerationMetadataParser.ParseFromPng(_tempFilePath), Is.Null);
        }

        [Test]
        public void ParseFromPng_WithWrongStealthMagic_FailsSafely()
        {
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildStealthPng(
                "{\"Software\":\"NovelAI\"}", invalidMagic: true));
            Assert.That(MediaGenerationMetadataParser.ParseFromPng(_tempFilePath), Is.Null);
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ParseFromPng_WithCorruptStealthPayload_FailsSafely(bool invalidLength, bool corruptGzip)
        {
            File.WriteAllBytes(_tempFilePath, TestPngBuilder.BuildStealthPng(
                "{\"Software\":\"NovelAI\"}", invalidLength: invalidLength, corruptGzip: corruptGzip));
            Assert.That(MediaGenerationMetadataParser.ParseFromPng(_tempFilePath), Is.Null);
        }

    }
}
