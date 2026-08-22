using System;
using System.Collections.Generic;
using Illustra.Models;

namespace Illustra.Helpers
{
    /// <summary>
    /// メディアファイルから埋め込み生成メタデータを取り出すファサード。
    /// MP4 の mdta メタデータ（"prompt" / "workflow" タグ）を
    /// <see cref="Mp4MetadataReader"/> で読み出し、
    /// <see cref="ComfyUIGraphAnalyzer"/> で解析して
    /// <see cref="GenerationMetadata"/> を構築する。
    /// </summary>
    public static class MediaGenerationMetadataParser
    {
        /// <summary>
        /// MP4 ファイルから生成メタデータを取得する。
        /// 埋め込みが存在しない場合は null を返す。
        /// プロンプトの解析に失敗した場合でも、ワークフロー埋め込み情報のみを持つ
        /// インスタンスを返す（ComfyUI で開けば内容を確認できるため）。
        /// </summary>
        public static GenerationMetadata ParseFromMp4(string filePath)
        {
            if (!Mp4MetadataReader.TryReadTags(filePath, out var tags))
                return null;

            bool hasPromptTag = tags.TryGetValue("prompt", out string promptJson);
            tags.TryGetValue("workflow", out string workflowJson);

            if (!hasPromptTag && workflowJson == null)
                return null; // 対象タグなし

            GenerationMetadata metadata = null;

            // prompt JSON（API形式）をグラフ解析
            if (hasPromptTag)
            {
                metadata = ComfyUIGraphAnalyzer.Analyze(promptJson);
            }

            // workflow JSON（GUI形式）にもプロンプトが含まれるため、
            // 解析できていない場合はフォールバックで試す
            if ((metadata == null || !metadata.ParseSuccess) && workflowJson != null)
            {
                var fallback = ComfyUIGraphAnalyzer.Analyze(workflowJson);
                if (fallback != null && (metadata == null ||
                    (!metadata.ParseSuccess && fallback.ParseSuccess)))
                {
                    metadata = metadata == null ? fallback : Merge(metadata, fallback);
                }
            }

            if (metadata == null)
            {
                // 解析失敗でもワークフロー埋め込み自体は表示対象にする
                metadata = new GenerationMetadata { Generator = "ComfyUI", ParseSuccess = false };
            }

            // 生 workflow は ComfyUI で再利用できる GUI 形式を優先
            if (workflowJson != null)
            {
                metadata.RawWorkflowJson = workflowJson;
            }

            return metadata;
        }

        /// <summary>
        /// prompt 由来の結果と workflow 由来の結果を統合する
        /// </summary>
        private static GenerationMetadata Merge(GenerationMetadata baseMeta, GenerationMetadata fallback)
        {
            if (string.IsNullOrEmpty(baseMeta.Prompt) && !string.IsNullOrEmpty(fallback.Prompt))
                baseMeta.Prompt = fallback.Prompt;

            if (string.IsNullOrEmpty(baseMeta.NegativePrompt) && !string.IsNullOrEmpty(fallback.NegativePrompt))
                baseMeta.NegativePrompt = fallback.NegativePrompt;

            if (string.IsNullOrEmpty(baseMeta.ModelName) && !string.IsNullOrEmpty(fallback.ModelName))
                baseMeta.ModelName = fallback.ModelName;

            foreach (var lora in fallback.Loras)
            {
                if (!baseMeta.Loras.Contains(lora))
                    baseMeta.Loras.Add(lora);
            }

            foreach (var parameter in fallback.Parameters)
            {
                if (!baseMeta.Parameters.ContainsKey(parameter.Key))
                    baseMeta.Parameters[parameter.Key] = parameter.Value;
            }

            baseMeta.ParseSuccess = !string.IsNullOrEmpty(baseMeta.Prompt) ||
                                    !string.IsNullOrEmpty(baseMeta.ModelName);
            return baseMeta;
        }
    }
}
