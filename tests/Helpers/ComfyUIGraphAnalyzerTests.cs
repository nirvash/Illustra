using System;
using Illustra.Helpers;
using NUnit.Framework;

namespace Illustra.Tests.Helpers
{
    /// <summary>
    /// ComfyUIGraphAnalyzer（リンク辿りによるプロンプト抽出）のテスト。
    /// Issue #50 の要件を検証する:
    /// - ノード構成が作成者によって異なっても、接続トポロジからプロンプトを特定できること
    /// - TE に接続されサンプラー等につながる経路上の文字列ノードのみを採用すること
    /// - 未接続のテキストノードやメモノードをプロンプトとみなさないこと
    /// </summary>
    [TestFixture]
    public class ComfyUIGraphAnalyzerTests
    {
        /// <summary>
        /// 実際の MiniMax H3 ファイルと同じ構造のワークフロー。
        /// PrimitiveStringMultiline → 動画モデルノード(prompt+clip 入力) → BasicGuider → SamplerCustomAdvanced
        /// </summary>
        private const string H3StyleWorkflowJson = @"{
  ""92"": { ""inputs"": { ""filename_prefix"": ""video/out"", ""format"": ""auto"", ""video"": [""150"", 0] }, ""class_type"": ""SaveVideo"", ""_meta"": { ""title"": ""ビデオを保存"" } },
  ""119"": { ""inputs"": { ""vae_name"": ""h3_vae.safetensors"" }, ""class_type"": ""VAELoader"" },
  ""123"": { ""inputs"": { ""sampler_name"": ""euler"" }, ""class_type"": ""KSamplerSelect"" },
  ""124"": { ""inputs"": { ""scheduler"": ""simple"", ""steps"": 8 }, ""class_type"": ""BasicScheduler"" },
  ""125"": { ""inputs"": { ""noise"": [""129"", 0], ""guider"": [""126"", 0], ""sampler"": [""123"", 0], ""sigmas"": [""124"", 0] }, ""class_type"": ""SamplerCustomAdvanced"" },
  ""126"": { ""inputs"": { ""model"": [""144"", 0], ""positive"": [""136"", 0] }, ""class_type"": ""BasicGuider"" },
  ""127"": { ""inputs"": { ""unet_name"": ""minimax_h3.safetensors"" }, ""class_type"": ""UNETLoader"" },
  ""128"": { ""inputs"": { ""clip_name"": ""h3_clip.safetensors"" }, ""class_type"": ""CLIPLoader"" },
  ""129"": { ""inputs"": { ""noise_seed"": 42 }, ""class_type"": ""RandomNoise"" },
  ""136"": { ""inputs"": { ""prompt"": [""165"", 0], ""clip"": [""128"", 0], ""vae"": [""119"", 0], ""length"": [""131"", 1], ""ref_images_ref_image_0"": [""137"", 0] }, ""class_type"": ""MiniMaxH3ReferenceToVideo"" },
  ""137"": { ""inputs"": { ""image"": ""reference_photo.png"" }, ""class_type"": ""LoadImage"" },
  ""144"": { ""inputs"": { ""lora_name"": ""first_lora.safetensors"", ""model"": [""161"", 0] }, ""class_type"": ""LoraLoaderModelOnly"" },
  ""150"": { ""inputs"": { ""images"": [""149"", 0], ""audio"": [""148"", 0] }, ""class_type"": ""CreateVideo"" },
  ""161"": { ""inputs"": { ""lora_name"": ""lightx2v_turbo.safetensors"", ""model"": [""127"", 0] }, ""class_type"": ""LoraLoaderModelOnly"" },
  ""162"": { ""inputs"": { ""model"": [""161"", 0], ""shift"": 8.0 }, ""class_type"": ""ModelSamplingMiniMaxH3"" },
  ""165"": { ""inputs"": { ""value"": ""A picture book story scene with a three-dimensional character."" }, ""class_type"": ""PrimitiveStringMultiline"", ""_meta"": { ""title"": ""Input Text (15sec)"" } },
  ""167"": { ""inputs"": { ""value"": ""この文字列は未接続なので採用されないはず"" }, ""class_type"": ""PrimitiveStringMultiline"", ""_meta"": { ""title"": ""Input Text (5sec)"" } },
  ""200"": { ""inputs"": {}, ""class_type"": ""MarkdownNote"", ""_meta"": { ""title"": ""Note"" } }
}";

        /// <summary>
        /// 標準的な Stable Diffusion ワークフロー。
        /// CLIPTextEncode のインライン text を KSampler の positive/negative で参照する構成
        /// </summary>
        private const string StandardSdWorkflowJson = @"{
  ""3"": { ""inputs"": { ""seed"": 12345, ""steps"": 28, ""cfg"": 7.5, ""sampler_name"": ""dpmpp_2m"", ""scheduler"": ""karras"", ""denoise"": 1.0, ""model"": [""4"", 0], ""positive"": [""5"", 0], ""negative"": [""6"", 0], ""latent_image"": [""7"", 0] }, ""class_type"": ""KSampler"" },
  ""4"": { ""inputs"": { ""ckpt_name"": ""sd_xl_base.safetensors"" }, ""class_type"": ""CheckpointLoaderSimple"" },
  ""5"": { ""inputs"": { ""text"": ""masterpiece, best quality, a girl in a flower field"", ""clip"": [""4"", 1] }, ""class_type"": ""CLIPTextEncode"" },
  ""6"": { ""inputs"": { ""text"": ""bad quality, worst quality"", ""clip"": [""4"", 1] }, ""class_type"": ""CLIPTextEncode"" },
  ""7"": { ""inputs"": { ""width"": 1024, ""height"": 1024, ""batch_size"": 1 }, ""class_type"": ""EmptyLatentImage"" },
  ""8"": { ""inputs"": { ""samples"": [""3"", 0], ""vae"": [""4"", 2] }, ""class_type"": ""VAEDecode"" },
  ""9"": { ""inputs"": { ""filename_prefix"": ""ComfyUI"", ""images"": [""8"", 0] }, ""class_type"": ""SaveImage"" }
}";

        [Test]
        public void Analyze_H3StyleWorkflow_ExtractsLinkedPrompt()
        {
            // Act
            var result = ComfyUIGraphAnalyzer.Analyze(H3StyleWorkflowJson);

            // Assert: リンクで接続されたノード 165 の本文が採用される
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Generator, Is.EqualTo("ComfyUI"));
            Assert.That(result.Prompt, Does.Contain("picture book"));
            Assert.That(result.Prompt, Is.Not.Contains("未接続"));
        }

        [Test]
        public void Analyze_H3StyleWorkflow_ExcludesUnconnectedTextAndNotes()
        {
            // Act
            var result = ComfyUIGraphAnalyzer.Analyze(H3StyleWorkflowJson);

            // Assert: 未接続の PrimitiveStringMultiline や参照画像ファイル名を採らない
            Assert.That(result.Prompt, Is.Not.EqualTo("この文字列は未接続なので採用されないはず"));
            Assert.That(result.Prompt, Is.Not.EqualTo("reference_photo.png"));
        }

        [Test]
        public void Analyze_H3StyleWorkflow_ExtractsModelAndLorasFromChain()
        {
            // Act
            var result = ComfyUIGraphAnalyzer.Analyze(H3StyleWorkflowJson);

            // Assert: model チェーン (Guider → LoRA ×2 → UNETLoader) を逆方向に辿れる
            Assert.That(result.ModelName, Is.EqualTo("minimax_h3.safetensors"));
            Assert.That(result.Loras, Does.Contain("first_lora.safetensors"));
            Assert.That(result.Loras, Does.Contain("lightx2v_turbo.safetensors"));
        }

        [Test]
        public void Analyze_H3StyleWorkflow_ExtractsParameters()
        {
            // Act
            var result = ComfyUIGraphAnalyzer.Analyze(H3StyleWorkflowJson);

            // Assert
            Assert.That(result.Parameters["steps"], Is.EqualTo("8"));
            Assert.That(result.Parameters["sampler_name"], Is.EqualTo("euler"));
            Assert.That(result.Parameters["scheduler"], Is.EqualTo("simple"));
            Assert.That(result.Parameters["seed"], Is.EqualTo("42")); // noise_seed が正規化される
        }

        [Test]
        public void Analyze_H3StyleWorkflow_ParseSuccessAndWorkflowKept()
        {
            // Act
            var result = ComfyUIGraphAnalyzer.Analyze(H3StyleWorkflowJson);

            // Assert
            Assert.That(result.ParseSuccess, Is.True);
            Assert.That(result.HasWorkflow, Is.True);
            Assert.That(result.RawWorkflowJson, Is.EqualTo(H3StyleWorkflowJson));
        }

        [Test]
        public void Analyze_StandardSdWorkflow_ExtractsPositiveAndNegative()
        {
            // Act
            var result = ComfyUIGraphAnalyzer.Analyze(StandardSdWorkflowJson);

            // Assert
            Assert.That(result.Prompt, Is.EqualTo("masterpiece, best quality, a girl in a flower field"));
            Assert.That(result.NegativePrompt, Is.EqualTo("bad quality, worst quality"));
            Assert.That(result.ModelName, Is.EqualTo("sd_xl_base.safetensors"));
        }

        [Test]
        public void Analyze_StandardSdWorkflow_ExtractsSamplerParameters()
        {
            // Act
            var result = ComfyUIGraphAnalyzer.Analyze(StandardSdWorkflowJson);

            // Assert
            Assert.That(result.Parameters["seed"], Is.EqualTo("12345"));
            Assert.That(result.Parameters["steps"], Is.EqualTo("28"));
            Assert.That(result.Parameters["cfg"], Is.EqualTo("7.5"));
            Assert.That(result.Parameters["sampler_name"], Is.EqualTo("dpmpp_2m"));
        }

        [Test]
        public void Analyze_WorkflowWithoutSamplers_FallsBackToTextNodeScan()
        {
            // Arrange: サンプラー等が存在しない不完全なグラフ
            const string json = @"{
  ""1"": { ""inputs"": { ""text"": ""orphan inline prompt"" }, ""class_type"": ""CLIPTextEncode"" }
}";

            // Act
            var result = ComfyUIGraphAnalyzer.Analyze(json);

            // Assert: フォールバックで text 入力を持つノードを見つける
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Prompt, Is.EqualTo("orphan inline prompt"));
        }

        [Test]
        public void Analyze_WithOnlyAssetStrings_PromptStaysEmpty()
        {
            // Arrange: プロンプトらしき文字列がなく、アセットパスだけの場合
            const string json = @"{
  ""1"": { ""inputs"": { ""image"": ""photo.png"" }, ""class_type"": ""LoadImage"" }
}";

            // Act
            var result = ComfyUIGraphAnalyzer.Analyze(json);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Prompt, Is.Null.Or.Empty);
            Assert.That(result.ParseSuccess, Is.False);
        }

        [Test]
        public void Analyze_WithInvalidJson_ReturnsNull()
        {
            // Act / Assert
            Assert.That(ComfyUIGraphAnalyzer.Analyze("not a json"), Is.Null);
            Assert.That(ComfyUIGraphAnalyzer.Analyze("{}"), Is.Null);
            Assert.That(ComfyUIGraphAnalyzer.Analyze(null), Is.Null);
            Assert.That(ComfyUIGraphAnalyzer.Analyze(string.Empty), Is.Null);
        }

        [Test]
        public void Analyze_WithDeepLinkChain_ResolvesWithinDepthLimit()
        {
            // Arrange: 中間ノードを挟んだ多段リンク
            const string json = @"{
  ""1"": { ""inputs"": { ""positive"": [""2"", 0] }, ""class_type"": ""KSampler"" },
  ""2"": { ""inputs"": { ""conditioning"": [""3"", 0] }, ""class_type"": ""ConditioningPassthrough"" },
  ""3"": { ""inputs"": { ""text"": ""deep chained prompt"" }, ""class_type"": ""CLIPTextEncode"" }
}";

            // Act
            var result = ComfyUIGraphAnalyzer.Analyze(json);

            // Assert
            Assert.That(result.Prompt, Is.EqualTo("deep chained prompt"));
        }

        [Test]
        public void Analyze_WithJsonNullParameter_DoesNotThrowAndSkipsValue()
        {
            // Arrange: "cfg": null のように JSON null を含むグラフ。
            // System.Text.Json では JSON null は null JsonNode 参照になるため、
            // ContainsKey が true を返しても値の取得結果は null になる（旧実装では NRE）
            const string json = @"{
  ""1"": { ""inputs"": { ""steps"": 6, ""cfg"": null, ""sampler_name"": ""euler"" }, ""class_type"": ""KSampler"" }
}";

            // Act: 例外が発生しないこと
            var result = ComfyUIGraphAnalyzer.Analyze(json);

            // Assert: JSON null はスキップされ、他のパラメータは収集される
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Parameters.ContainsKey("cfg"), Is.False);
            Assert.That(result.Parameters["steps"], Is.EqualTo("6"));
            Assert.That(result.Parameters["sampler_name"], Is.EqualTo("euler"));
        }

        [Test]
        public void Analyze_WithMalformedNodes_DoesNotThrow()
        {
            // Arrange: 想定外の構造（配列ノード / inputs がスカラー / null 値の混在）
            const string json = @"{
  ""a"": [1, 2, 3],
  ""b"": { ""inputs"": 5, ""class_type"": null },
  ""c"": { ""inputs"": { ""text"": null, ""seed"": null, ""model"": [""nope"", 0] }, ""class_type"": ""KSampler"" }
}";

            // Act / Assert: 例外を外部に漏らさない
            Assert.DoesNotThrow(() => ComfyUIGraphAnalyzer.Analyze(json));
        }
    }
}
