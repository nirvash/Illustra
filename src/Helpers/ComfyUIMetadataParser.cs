using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Linq;

namespace StableDiffusionTools
{
    /// <summary>
    /// ComfyUI形式のStable Diffusionメタデータを解析するパーサー
    /// </summary>
    public class ComfyUIMetadataParser : IStableDiffusionMetadataParser
    {
        /// <summary>
        /// ComfyUI形式のメタデータを解析する
        /// </summary>
        public StableDiffusionMetadata Parse(string metadataText)
        {
            var metadata = new StableDiffusionMetadata();
            metadata.RawMetadata = metadataText;
            metadata.Generator = "ComfyUI";

            try
            {
                if (string.IsNullOrWhiteSpace(metadataText))
                {
                    return metadata;
                }

                // JSONノードとしてパース
                var root = JsonNode.Parse(metadataText)?.AsObject();
                if (root == null)
                {
                    return metadata; // JSONでない場合は解析失敗
                }

                // ノードIDとクラスタイプのマッピングを作成
                var nodeTypes = new Dictionary<string, string>();
                var clipNodes = new Dictionary<string, JsonObject>();
                var checkpointNodes = new List<JsonObject>();
                var vaeNodes = new List<JsonObject>();
                var samplerNodes = new List<string>();

                // まずはノードのタイプを特定
                foreach (var node in root)
                {
                    var nodeObj = node.Value?.AsObject();
                    if (nodeObj == null) continue;

                    string classType = nodeObj["class_type"]?.ToString();
                    if (string.IsNullOrEmpty(classType)) continue;

                    nodeTypes[node.Key] = classType;

                    // CLIPTextEncodeノードを収集（標準的なノード名）
                    if (classType == "CLIPTextEncode")
                    {
                        clipNodes[node.Key] = nodeObj;
                    }
                    // KSamplerなどのサンプラーノードを収集
                    else if (classType.Contains("Sampler") || classType.Contains("sampler"))
                    {
                        samplerNodes.Add(node.Key);
                    }
                    // チェックポイントローダーを収集
                    else if (classType.Contains("Checkpoint") || classType.Contains("checkpoint") ||
                             classType.Contains("Model") || classType.Contains("model"))
                    {
                        checkpointNodes.Add(nodeObj);
                    }
                    // VAEノードを収集
                    else if (classType.Contains("VAE") || classType.Contains("vae"))
                    {
                        vaeNodes.Add(nodeObj);
                    }
                }

                // フォールバック：標準的なclass_typeでない場合も探索
                if (clipNodes.Count == 0)
                {
                    // 各ノードをチェックして、テキスト入力を持つノードを探す
                    foreach (var node in root)
                    {
                        var nodeObj = node.Value?.AsObject();
                        if (nodeObj == null) continue;

                        var inputs = nodeObj["inputs"]?.AsObject();
                        if (inputs == null) continue;

                        // テキスト入力があるノードをCLIPTextEncodeとして扱う
                        if (inputs["text"] != null || inputs["prompt"] != null)
                        {
                            clipNodes[node.Key] = nodeObj;
                        }
                    }
                }

                string positivePrompt = null;
                string negativePrompt = null;
                string modelName = null;
                string positiveNodeId = null;
                string negativeNodeId = null;

                // サンプラーノードから正負のプロンプトノードIDを取得
                foreach (var samplerNodeId in samplerNodes)
                {
                    if (!root.ContainsKey(samplerNodeId)) continue;

                    var samplerObj = root[samplerNodeId].AsObject();
                    var inputs = samplerObj["inputs"]?.AsObject();
                    if (inputs == null) continue;

                    // positive/negativeノードIDを取得
                    var foundPositive = TryGetNodeRefId(inputs, "positive", out string posId);
                    var foundNegative = TryGetNodeRefId(inputs, "negative", out string negId);

                    if (foundPositive)
                    {
                        positiveNodeId = posId;
                    }

                    if (foundNegative)
                    {
                        negativeNodeId = negId;
                    }

                    // モデル参照がある場合
                    if (TryGetNodeRefId(inputs, "model", out string modelNodeId) &&
                        !string.IsNullOrEmpty(modelNodeId) &&
                        nodeTypes.TryGetValue(modelNodeId, out string modelNodeType))
                    {
                        // LoRAローダーの場合
                        if (modelNodeType.Contains("Lora") || modelNodeType.Contains("lora"))
                        {
                            ExtractLoraInfo(root, modelNodeId, metadata);
                        }
                    }

                    // 見つかったら終了
                    if (foundPositive || foundNegative)
                    {
                        break;
                    }
                }

                // プロンプトノードから直接プロンプトを抽出
                if (!string.IsNullOrEmpty(positiveNodeId))
                {
                    positivePrompt = ExtractPromptFromNode(root, positiveNodeId, false);
                }

                if (!string.IsNullOrEmpty(negativeNodeId))
                {
                    negativePrompt = ExtractPromptFromNode(root, negativeNodeId, true);
                }

                // フォールバック：サンプラーノードからの参照が見つからない場合
                if (string.IsNullOrEmpty(positiveNodeId) && string.IsNullOrEmpty(negativeNodeId))
                {
                    // CLIPノードを直接探して使用
                    if (clipNodes.Count >= 2)
                    {
                        // 2つ以上あれば最初をポジティブ、2番目をネガティブとみなす
                        var enumerator = clipNodes.GetEnumerator();
                        if (enumerator.MoveNext())
                        {
                            positiveNodeId = enumerator.Current.Key;
                            if (enumerator.MoveNext())
                            {
                                negativeNodeId = enumerator.Current.Key;
                            }
                        }
                    }
                    else if (clipNodes.Count == 1)
                    {
                        // 1つしかなければポジティブとみなす
                        positiveNodeId = clipNodes.Keys.First();
                    }
                }

                // CLIPTextEncodeノードからプロンプトテキストを取得
                if (!string.IsNullOrEmpty(positiveNodeId) && clipNodes.TryGetValue(positiveNodeId, out var posClipNode))
                {
                    // textまたはpromptフィールドからプロンプトを取得
                    positivePrompt = posClipNode["inputs"]?["text"]?.ToString();
                    if (string.IsNullOrEmpty(positivePrompt))
                    {
                        positivePrompt = posClipNode["inputs"]?["prompt"]?.ToString();
                    }
                }

                if (!string.IsNullOrEmpty(negativeNodeId) && clipNodes.TryGetValue(negativeNodeId, out var negClipNode))
                {
                    // textまたはpromptフィールドからプロンプトを取得
                    negativePrompt = negClipNode["inputs"]?["text"]?.ToString();
                    if (string.IsNullOrEmpty(negativePrompt))
                    {
                        negativePrompt = negClipNode["inputs"]?["prompt"]?.ToString();
                    }
                }

                // モデル名がまだ見つかっていない場合、CheckpointLoaderからモデル名を取得
                if (string.IsNullOrEmpty(modelName) && checkpointNodes.Count > 0)
                {
                    foreach (var checkpoint in checkpointNodes)
                    {
                        // ckpt_nameまたはチェックポイント名が格納されそうなフィールド名を確認
                        modelName = checkpoint["inputs"]?["ckpt_name"]?.ToString();
                        if (string.IsNullOrEmpty(modelName))
                        {
                            modelName = checkpoint["inputs"]?["name"]?.ToString();
                        }
                        if (string.IsNullOrEmpty(modelName))
                        {
                            modelName = checkpoint["inputs"]?["model_name"]?.ToString();
                        }

                        if (!string.IsNullOrEmpty(modelName))
                            break;
                    }
                }

                // モデル名がまだ見つかっていない場合、VAEローダーからVAE名を取得
                if (string.IsNullOrEmpty(modelName) && vaeNodes.Count > 0)
                {
                    foreach (var vae in vaeNodes)
                    {
                        // VAEノードからVAE名を取得
                        modelName = vae["inputs"]?["vae_name"]?.ToString();
                        if (string.IsNullOrEmpty(modelName))
                        {
                            modelName = vae["inputs"]?["name"]?.ToString();
                        }

                        if (!string.IsNullOrEmpty(modelName))
                            break;
                    }
                }

                // さらにモデル名が見つからない場合、すべてのノードの入力をチェック
                if (string.IsNullOrEmpty(modelName))
                {
                    foreach (var node in root)
                    {
                        var nodeObj = node.Value?.AsObject();
                        if (nodeObj == null) continue;

                        var inputs = nodeObj["inputs"]?.AsObject();
                        if (inputs == null) continue;

                        // 可能性のあるフィールド名を確認
                        foreach (var fieldName in new[] { "ckpt_name", "model_name", "vae_name", "name" })
                        {
                            if (inputs.ContainsKey(fieldName))
                            {
                                modelName = inputs[fieldName]?.ToString();
                                if (!string.IsNullOrEmpty(modelName))
                                    break;
                            }
                        }

                        if (!string.IsNullOrEmpty(modelName))
                            break;
                    }
                }

                // フォールバック: それでもプロンプトが見つからない場合は、すべてのノードを検索
                if (string.IsNullOrEmpty(positivePrompt))
                {
                    positivePrompt = FindPromptLikeStringInNodes(root);
                }

                // ネガティブプロンプトが見つからない場合の特別処理
                if (string.IsNullOrEmpty(negativePrompt) || negativePrompt == positivePrompt)
                {
                    // サンプラーノードを調べて、negative入力を持つノードを探す
                    foreach (var samplerNodeId in samplerNodes)
                    {
                        if (!root.ContainsKey(samplerNodeId)) continue;

                        var samplerObj = root[samplerNodeId].AsObject();
                        var inputs = samplerObj["inputs"]?.AsObject();
                        if (inputs == null) continue;

                        // ネガティブプロンプト関連の文字列を探す
                        foreach (var input in inputs)
                        {
                            if (input.Key.Contains("negative") || input.Key.Contains("neg"))
                            {
                                // 配列形式のノード参照 [nodeId, outputIndex] の場合
                                if (input.Value is JsonArray valueArray && valueArray.Count > 0)
                                {
                                    string refNodeId = valueArray[0]?.ToString();
                                    if (!string.IsNullOrEmpty(refNodeId) && root.ContainsKey(refNodeId))
                                    {
                                        // 参照先ノードからプロンプトを抽出
                                        string refPrompt = ExtractPromptFromNode(root, refNodeId, true);
                                        if (!string.IsNullOrEmpty(refPrompt) && refPrompt != positivePrompt)
                                        {
                                            negativePrompt = refPrompt;
                                            break;
                                        }
                                    }
                                }
                                // 文字列値の場合
                                else
                                {
                                    var value = input.Value?.ToString();
                                    if (!string.IsNullOrEmpty(value) && value != positivePrompt)
                                    {
                                        // embedding:FastNegativeV2 のような形式を検出
                                        if (value.Contains("embedding:") || value.Contains("FastNegative"))
                                        {
                                            negativePrompt = value;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(negativePrompt) && negativePrompt != positivePrompt)
                            break;
                    }
                }

                // さらにフォールバック: すべてのノードからネガティブプロンプトらしきものを探す
                if (string.IsNullOrEmpty(negativePrompt) || negativePrompt == positivePrompt)
                {
                    negativePrompt = FindNegativePromptInNodes(root, positivePrompt);
                }

                // メタデータを設定
                if (!string.IsNullOrEmpty(positivePrompt))
                    metadata.PositivePrompt = positivePrompt;

                if (!string.IsNullOrEmpty(negativePrompt))
                    metadata.NegativePrompt = negativePrompt;

                if (!string.IsNullOrEmpty(modelName))
                    metadata.ModelName = modelName;

                metadata.ParseSuccess = !string.IsNullOrEmpty(positivePrompt) || !string.IsNullOrEmpty(modelName);
            }
            catch (Exception ex)
            {
                metadata.ParseSuccess = false;
                System.Diagnostics.Debug.WriteLine($"ComfyUIパース失敗: {ex.Message}");
            }

            return metadata;
        }

        /// <summary>
        /// ノードまたはその参照先からプロンプト情報を抽出
        /// </summary>
        /// <param name="root">ルートJSONオブジェクト</param>
        /// <param name="nodeId">ノードID</param>
        /// <param name="isNegative">ネガティブプロンプトを探すかどうか</param>
        private string ExtractPromptFromNode(JsonObject root, string nodeId, bool isNegative = false)
        {
            if (!root.ContainsKey(nodeId)) return null;

            var nodeObj = root[nodeId].AsObject();
            var inputs = nodeObj["inputs"]?.AsObject();
            if (inputs == null) return null;

            // 直接プロンプトフィールドを確認
            string prompt = inputs["prompt"]?.ToString();
            if (!string.IsNullOrEmpty(prompt)) return prompt;

            prompt = inputs["text"]?.ToString();
            if (!string.IsNullOrEmpty(prompt)) return prompt;

            // 参照元ノードのキー候補 (正負プロンプトで切り替える)
            string[] possibleRefKeys;
            if (isNegative)
            {
                possibleRefKeys = new[] { "negative", "neg", "neg_ctx", "negative_ctx" };
            }
            else
            {
                possibleRefKeys = new[] { "base_ctx", "clip", "context", "positive" };
            }

            foreach (var key in possibleRefKeys)
            {
                if (TryGetNodeRefId(inputs, key, out string refNodeId) && !string.IsNullOrEmpty(refNodeId))
                {
                    // 再帰的に参照先をチェック (最大15階層まで)
                    for (int i = 0; i < 15; i++)
                    {
                        string refPrompt = ExtractPromptFromNode(root, refNodeId, isNegative);
                        if (!string.IsNullOrEmpty(refPrompt)) return refPrompt;

                        // さらに参照先がある場合
                        if (!root.ContainsKey(refNodeId)) break;

                        var refNodeObj = root[refNodeId].AsObject();
                        var refInputs = refNodeObj["inputs"]?.AsObject();
                        if (refInputs == null) break;

                        bool foundNextRef = false;
                        foreach (var refKey in possibleRefKeys)
                        {
                            if (TryGetNodeRefId(refInputs, refKey, out string nextRefNodeId) && !string.IsNullOrEmpty(nextRefNodeId))
                            {
                                refNodeId = nextRefNodeId;
                                foundNextRef = true;
                                break;
                            }
                        }

                        if (!foundNextRef) break;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// すべてのノードからプロンプトらしい文字列を探す
        /// </summary>
        private string FindPromptLikeStringInNodes(JsonObject root)
        {
            foreach (var node in root)
            {
                var nodeObj = node.Value?.AsObject();
                if (nodeObj == null) continue;

                var classType = nodeObj["class_type"]?.ToString();
                var inputs = nodeObj["inputs"]?.AsObject();
                if (inputs == null) continue;

                // "Power Prompt"のようなクラス名を持つノードを優先
                bool isPriority = !string.IsNullOrEmpty(classType) &&
                    (classType.Contains("Prompt") || classType.Contains("prompt") ||
                     classType.Contains("Text") || classType.Contains("text"));

                // すべての入力フィールドを確認
                foreach (var input in inputs)
                {
                    // プロンプトっぽいキー名
                    bool isPromptKey = input.Key.Contains("prompt") || input.Key.Contains("text");
                    if (!isPromptKey && !isPriority) continue;

                    string value = input.Value?.ToString();
                    if (string.IsNullOrEmpty(value)) continue;

                    // プロンプトらしい特徴: カンマ区切り、キーワード、長さ
                    if ((value.Contains(",") || value.Contains("(") || value.Contains(")")) &&
                        value.Length > 10 &&
                        !value.StartsWith("[") && // 配列を除外
                        !value.Contains("\"class_type\"")) // JSONを除外
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// すべてのノードからネガティブプロンプトらしい文字列を探す
        /// </summary>
        private string FindNegativePromptInNodes(JsonObject root, string positivePrompt)
        {
            foreach (var node in root)
            {
                var nodeObj = node.Value?.AsObject();
                if (nodeObj == null) continue;

                var classType = nodeObj["class_type"]?.ToString();
                var inputs = nodeObj["inputs"]?.AsObject();
                if (inputs == null) continue;

                // "Negative"や"Embedding"を含むクラス名を優先
                bool isPriority = !string.IsNullOrEmpty(classType) &&
                    (classType.Contains("Negative") || classType.Contains("negative") ||
                     classType.Contains("Embedding") || classType.Contains("embedding"));

                // すべての入力フィールドを確認
                foreach (var input in inputs)
                {
                    // ネガティブプロンプトっぽいキー名
                    bool isNegativeKey = input.Key.Contains("negative") ||
                                         input.Key.Contains("neg") ||
                                         input.Key.Contains("embedding");

                    if (!isNegativeKey && !isPriority) continue;

                    string value = input.Value?.ToString();
                    if (string.IsNullOrEmpty(value) || value == positivePrompt) continue;

                    // ネガティブプロンプトらしい特徴: embedding関連、特定のパターン
                    if (value.Contains("embedding:") || value.Contains("FastNegative") ||
                        (value.Contains(",") && value.Length > 5))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 入力オブジェクトから参照ノードIDを取得
        /// </summary>
        private bool TryGetNodeRefId(JsonObject inputs, string key, out string nodeId)
        {
            nodeId = null;
            if (inputs == null || !inputs.ContainsKey(key)) return false;

            var refNode = inputs[key];
            if (refNode == null) return false;

            // 配列形式 [nodeId, outputIndex]
            if (refNode is JsonArray refArr && refArr.Count > 0)
            {
                nodeId = refArr[0]?.ToString();
                return !string.IsNullOrEmpty(nodeId);
            }
            // 文字列形式
            else if (refNode is JsonValue refVal)
            {
                nodeId = refVal.ToString();
                return !string.IsNullOrEmpty(nodeId);
            }

            return false;
        }

        /// <summary>
        /// LoRA情報を抽出する
        /// </summary>
        private void ExtractLoraInfo(JsonObject root, string loraNodeId, StableDiffusionMetadata metadata)
        {
            if (!root.ContainsKey(loraNodeId)) return;

            var loraObj = root[loraNodeId].AsObject();
            var loraInputs = loraObj["inputs"]?.AsObject();
            if (loraInputs == null) return;

            // LoRAからモデル名を取得
            string loraName = null;
            foreach (string possibleKey in new[] { "lora_name", "name", "lora" })
            {
                if (loraInputs.ContainsKey(possibleKey))
                {
                    loraName = loraInputs[possibleKey]?.ToString();
                    if (!string.IsNullOrEmpty(loraName)) break;
                }
            }

            if (!string.IsNullOrEmpty(loraName))
            {
                if (metadata.ModelLoras == null)
                {
                    metadata.ModelLoras = new List<string>();
                }

                if (!metadata.ModelLoras.Contains(loraName))
                {
                    metadata.ModelLoras.Add(loraName);
                }
            }

            // 元のモデルを追跡
            if (TryGetNodeRefId(loraInputs, "model", out string baseModelNodeId) &&
                !string.IsNullOrEmpty(baseModelNodeId) &&
                root.ContainsKey(baseModelNodeId))
            {
                var baseModelObj = root[baseModelNodeId].AsObject();
                var baseModelClass = baseModelObj["class_type"]?.ToString();

                if (!string.IsNullOrEmpty(baseModelClass) &&
                    (baseModelClass.Contains("Checkpoint") || baseModelClass.Contains("checkpoint") ||
                     baseModelClass.Contains("Model") || baseModelClass.Contains("model")))
                {
                    // モデル名を探す
                    var baseInputs = baseModelObj["inputs"]?.AsObject();
                    if (baseInputs != null)
                    {
                        foreach (string possibleKey in new[] { "ckpt_name", "name", "model_name" })
                        {
                            if (baseInputs.ContainsKey(possibleKey))
                            {
                                string modelName = baseInputs[possibleKey]?.ToString();
                                if (!string.IsNullOrEmpty(modelName))
                                {
                                    metadata.ModelName = modelName;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// このパーサーでメタデータを解析可能かどうかを判定する
        /// </summary>
        public bool CanParse(string metadataText)
        {
            if (string.IsNullOrWhiteSpace(metadataText))
                return false;

            try
            {
                // 簡易的なJSON形式チェック
                if (!(metadataText.Trim().StartsWith("{") && metadataText.Trim().EndsWith("}")))
                    return false;

                // ComfyUI形式の特徴的なパターンを確認
                return metadataText.Contains("\"class_type\"") &&
                       (metadataText.Contains("\"KSampler\"") ||
                        metadataText.Contains("\"CLIPTextEncode\"") ||
                        metadataText.Contains("\"Sampler\"") ||
                        metadataText.Contains("\"sampler\"") ||
                        metadataText.Contains("\"Checkpoint\""));
            }
            catch
            {
                return false;
            }
        }
    }
}