using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Illustra.Models;

namespace Illustra.Helpers
{
    /// <summary>
    /// ComfyUI の prompt JSON（ノードグラフ）を解析し、生成メタデータを抽出するクラス。
    ///
    /// ノード構成はワークフローの作成者により異なるため、特定のノード名に依存せず、
    /// グラフの接続トポロジからプロンプトを特定する（Issue #50）:
    /// - サンプラー / ガイダー / 出力系ノードから入力リンクを逆方向に辿る
    /// - TE（テキストエンコーダ）に接続され、かつその経路がサンプラー等に
    ///   接続されている文字列入力ノードをプロンプト本文とみなす
    /// - 接続されていない孤立テキストノード（MarkdownNote 等のメモ）は対象外
    /// </summary>
    public static class ComfyUIGraphAnalyzer
    {
        /// <summary>
        /// プロンプト本文として扱う入力キー
        /// </summary>
        private static readonly HashSet<string> TextInputKeys = new HashSet<string> { "text", "prompt", "value" };

        /// <summary>
        /// ファイルパス等のアセット文字列とみなす拡張子
        /// </summary>
        private static readonly string[] AssetExtensions =
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp",
            ".safetensors", ".ckpt", ".pt", ".pth", ".bin", ".gguf", ".sft",
            ".mp4", ".mov", ".avi", ".webm"
        };

        /// <summary>
        /// サンプリング・ガイダンス・出力系ノードかどうか（逆方向走査の起点）
        /// </summary>
        private static bool IsGenerationChainNode(string classType)
        {
            if (string.IsNullOrEmpty(classType))
                return false;

            return classType.Contains("Sampler") || classType.Contains("sampler") ||
                   classType.Contains("Guider") || classType.Contains("guider") ||
                   classType.StartsWith("Save") || classType.Contains("VideoCombine");
        }

        /// <summary>
        /// ネガティブ側とみなす入力キーかどうか
        /// </summary>
        private static bool IsNegativeKey(string key)
        {
            return key != null && (key.Contains("negative") || key.Contains("neg"));
        }

        /// <summary>
        /// prompt JSON を解析して GenerationMetadata を返す。
        /// 解析できない場合や想定外の例外が発生した場合は null を返す。
        /// </summary>
        public static GenerationMetadata Analyze(string promptJson)
        {
            if (string.IsNullOrWhiteSpace(promptJson))
                return null;

            try
            {
                return AnalyzeCore(promptJson);
            }
            catch (Exception ex)
            {
                // 破損・想定外形式のグラフで例外を外部に漏らさず、
                // 呼び出し元のワークフロー埋め込みフォールバックに委ねる
                System.Diagnostics.Debug.WriteLine($"ComfyUI グラフ解析エラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Analyze の本体。
        /// </summary>
        private static GenerationMetadata AnalyzeCore(string promptJson)
        {
            JsonObject root;
            try
            {
                root = JsonNode.Parse(promptJson) as JsonObject;
            }
            catch (JsonException)
            {
                return null;
            }

            if (root == null || root.Count == 0)
                return null;

            // ノードマップを構築: nodeId → { class_type, inputs }
            var nodeClassTypes = new Dictionary<string, string>();
            var nodeInputs = new Dictionary<string, JsonObject>();
            foreach (var entry in root)
            {
                var nodeObj = entry.Value as JsonObject;
                if (nodeObj == null)
                    continue;

                string id = entry.Key;
                nodeClassTypes[id] = nodeObj["class_type"]?.ToString() ?? string.Empty;
                nodeInputs[id] = nodeObj["inputs"] as JsonObject ?? new JsonObject();
            }

            var metadata = new GenerationMetadata
            {
                Generator = "ComfyUI",
                RawWorkflowJson = promptJson
            };

            ExtractPrompts(nodeClassTypes, nodeInputs, metadata);
            ExtractModelInfo(nodeClassTypes, nodeInputs, metadata);
            ExtractParameters(nodeClassTypes, nodeInputs, metadata);

            metadata.ParseSuccess = !string.IsNullOrEmpty(metadata.Prompt) ||
                                    !string.IsNullOrEmpty(metadata.ModelName);

            return metadata;
        }

        /// <summary>
        /// 生成系ノードから逆方向にリンクを辿り、プロンプト本文を収集する
        /// </summary>
        private static void ExtractPrompts(
            Dictionary<string, string> nodeClassTypes,
            Dictionary<string, JsonObject> nodeInputs,
            GenerationMetadata metadata)
        {
            var positives = new List<string>();
            var negatives = new List<string>();
            var visited = new HashSet<string>();

            // 走査起点: サンプラー / ガイダー / 出力系ノードの positive / negative / prompt 系入力
            foreach (var nodeId in nodeClassTypes.Keys)
            {
                if (!IsGenerationChainNode(nodeClassTypes[nodeId]))
                    continue;

                foreach (var input in nodeInputs[nodeId])
                {
                    bool isNegative = IsNegativeKey(input.Key);
                    bool isPositiveKey = input.Key.Contains("pos") && !input.Key.Contains("position");
                    bool isPromptish = isNegative || isPositiveKey || TextInputKeys.Contains(input.Key);
                    if (!isPromptish)
                        continue;

                    TraverseForText(input.Value, isNegative, positives, negatives, visited,
                                    nodeClassTypes, nodeInputs, depth: 0);
                }
            }

            metadata.Prompt = PickLongest(positives);
            metadata.NegativePrompt = PickLongest(negatives.Where(n => n != metadata.Prompt));

            // グラフ辿りで見つからない場合のフォールバック:
            // 未接続だが text 系入力を持つノードを探す（メモ系ノードは除外）
            if (metadata.Prompt == null && negatives.Count == 0)
            {
                var fallbackPositives = new List<string>();
                foreach (var nodeId in nodeClassTypes.Keys)
                {
                    string classType = nodeClassTypes[nodeId];
                    if (classType.Contains("Note"))
                        continue;

                    foreach (var input in nodeInputs[nodeId])
                    {
                        if (TextInputKeys.Contains(input.Key) &&
                            input.Value is JsonValue value &&
                            TryGetNonAssetString(value, out string text))
                        {
                            fallbackPositives.Add(text);
                        }
                    }
                }

                metadata.Prompt = PickLongest(fallbackPositives);
            }
        }

        /// <summary>
        /// リンク値またはスカラー値を再帰的にたどってプロンプト候補文字列を収集する
        /// </summary>
        private static void TraverseForText(
            JsonNode value,
            bool isNegative,
            List<string> positives,
            List<string> negatives,
            HashSet<string> visited,
            Dictionary<string, string> nodeClassTypes,
            Dictionary<string, JsonObject> nodeInputs,
            int depth)
        {
            if (depth > 30)
                return;

            // リンク参照 ["nodeId", outputIndex] の場合
            if (value is JsonArray linkArray && linkArray.Count >= 1)
            {
                string refNodeId = linkArray[0]?.ToString();
                if (!string.IsNullOrEmpty(refNodeId) && nodeClassTypes.ContainsKey(refNodeId) && visited.Add(refNodeId))
                {
                    foreach (var input in nodeInputs[refNodeId])
                    {
                        // スカラー文字列ならプロンプト候補
                        if (TextInputKeys.Contains(input.Key) &&
                            input.Value is JsonValue scalar &&
                            TryGetNonAssetString(scalar, out string text))
                        {
                            (isNegative ? negatives : positives).Add(text);
                        }
                        else
                        {
                            // さらにリンクを逆方向に辿る（ネガティブ判定は上書きしない）
                            TraverseForText(input.Value, isNegative, positives, negatives, visited,
                                            nodeClassTypes, nodeInputs, depth + 1);
                        }
                    }
                }

                return;
            }

            // インライン文字列の場合（サンプラー直下の text/prompt 入力など）
            if (value is JsonValue inlineValue && TryGetNonAssetString(inlineValue, out string inlineText))
            {
                (isNegative ? negatives : positives).Add(inlineText);
            }
        }

        /// <summary>
        /// JsonValue からファイルパス等でない文字列を取り出す
        /// </summary>
        private static bool TryGetNonAssetString(JsonValue value, out string text)
        {
            text = null;

            if (!value.TryGetValue<string>(out string candidate))
                return false; // 数値等は対象外

            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            // アセットファイルパスらしき文字列は除外
            string lower = candidate.ToLowerInvariant();
            foreach (var ext in AssetExtensions)
            {
                if (lower.EndsWith(ext))
                    return false;
            }

            text = candidate;
            return true;
        }

        /// <summary>
        /// 候補の中から最長のものを選ぶ
        /// </summary>
        private static string PickLongest(IEnumerable<string> candidates)
        {
            string best = null;
            foreach (var candidate in candidates)
            {
                if (best == null || candidate.Length > best.Length)
                    best = candidate;
            }

            return best;
        }

        /// <summary>
        /// model リンクを逆方向に辿り、モデル名と LoRA を収集する
        /// </summary>
        private static void ExtractModelInfo(
            Dictionary<string, string> nodeClassTypes,
            Dictionary<string, JsonObject> nodeInputs,
            GenerationMetadata metadata)
        {
            string modelName = null;
            var loras = new List<string>();

            foreach (var nodeId in nodeClassTypes.Keys)
            {
                var inputs = nodeInputs[nodeId];
                if (!inputs.ContainsKey("model"))
                    continue;

                WalkModelChain(inputs["model"], nodeClassTypes, nodeInputs, loras, ref modelName, depth: 0);

                if (modelName != null)
                    break;
            }

            // model チェーンで見つからない場合はローダー系ノードを直接探索
            if (modelName == null)
            {
                foreach (var nodeId in nodeClassTypes.Keys)
                {
                    string classType = nodeClassTypes[nodeId];
                    var inputs = nodeInputs[nodeId];

                    modelName = TryGetLoaderFileName(classType, inputs, "CheckpointLoaderSimple", "ckpt_name") ??
                                TryGetLoaderFileName(classType, inputs, "UNETLoader", "unet_name") ??
                                modelName;
                    if (modelName != null)
                        break;
                }
            }

            metadata.ModelName = modelName ?? string.Empty;
            metadata.Loras = loras.Distinct().ToList();
        }

        /// <summary>
        /// model リンクチェーンを逆方向にたどる（LoRA → ... → チェックポイント/UNET）
        /// </summary>
        private static void WalkModelChain(
            JsonNode modelLink,
            Dictionary<string, string> nodeClassTypes,
            Dictionary<string, JsonObject> nodeInputs,
            List<string> loras,
            ref string modelName,
            int depth)
        {
            if (depth > 15 || modelName != null)
                return;

            if (!(modelLink is JsonArray linkArray) || linkArray.Count < 1)
                return;

            string refNodeId = linkArray[0]?.ToString();
            if (string.IsNullOrEmpty(refNodeId) || !nodeClassTypes.ContainsKey(refNodeId))
                return;

            string classType = nodeClassTypes[refNodeId];
            var inputs = nodeInputs[refNodeId];

            if (classType.Contains("Lora"))
            {
                if (inputs["lora_name"]?.ToString() is string loraName && !string.IsNullOrEmpty(loraName))
                    loras.Add(loraName);

                WalkModelChain(inputs.ContainsKey("model") ? inputs["model"] : null,
                               nodeClassTypes, nodeInputs, loras, ref modelName, depth + 1);
            }
            else if (classType.Contains("Checkpoint"))
            {
                modelName = inputs["ckpt_name"]?.ToString() ?? modelName;
            }
            else if (classType.Contains("UNETLoader") || classType == "LoadModel")
            {
                modelName = inputs["unet_name"]?.ToString() ?? modelName;
            }
            else
            {
                // 中間ノードの場合はさらに手前へ
                if (inputs.ContainsKey("model"))
                {
                    WalkModelChain(inputs["model"], nodeClassTypes, nodeInputs, loras, ref modelName, depth + 1);
                }
            }
        }

        private static string TryGetLoaderFileName(string classType, JsonObject inputs, string loaderType, string fieldName)
        {
            if (classType == loaderType)
                return inputs[fieldName]?.ToString();
            return null;
        }

        /// <summary>
        /// サンプラー系パラメータ（steps / cfg / seed 等）を収集する
        /// </summary>
        private static void ExtractParameters(
            Dictionary<string, string> nodeClassTypes,
            Dictionary<string, JsonObject> nodeInputs,
            GenerationMetadata metadata)
        {
            var knownParameterKeys = new[] { "steps", "cfg", "cfg_scale", "denoise", "seed", "noise_seed", "sampler_name", "scheduler", "shift" };

            foreach (var parameterKey in knownParameterKeys)
            {
                if (metadata.Parameters.ContainsKey(parameterKey))
                    continue;

                foreach (var nodeId in nodeClassTypes.Keys)
                {
                    var inputs = nodeInputs[nodeId];
                    if (!inputs.ContainsKey(parameterKey))
                        continue;

                    var value = inputs[parameterKey];

                    // JSON null ("cfg": null 等) は JsonNode の null 参照になるため除外
                    // リンク参照も除外（スカラー値のみ）
                    if (value is null || value is JsonArray)
                        continue;

                    string text = value.ToString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        metadata.Parameters[parameterKey] = text;
                        break;
                    }
                }
            }

            // seed は noise_seed が優先されるため seed へ正規化し、重複表示を避ける
            if (metadata.Parameters.ContainsKey("noise_seed"))
            {
                if (!metadata.Parameters.ContainsKey("seed"))
                {
                    metadata.Parameters["seed"] = metadata.Parameters["noise_seed"];
                }
                metadata.Parameters.Remove("noise_seed");
            }
        }
    }
}
