using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StableDiffusionTools
{
    /// <summary>
    /// WebUI形式のStable Diffusionメタデータを解析するパーサー
    /// </summary>
    public class WebUIMetadataParser : IStableDiffusionMetadataParser
    {
        /// <summary>
        /// 既定のセクションパターン
        /// </summary>
        private static readonly Dictionary<string, string> SectionPatterns = new()
        {
            { "Negative prompt:", "Negative prompt" },
            { "Steps:", "Parameters" }, // Steps以降のパラメータ群は全てParametersセクションとして扱う
        };

        /// <summary>
        /// パラメータセクションに含まれるキーのパターン
        /// </summary>
        private static readonly string[] ParameterKeys = new[]
        {
            "Steps:",
            "Sampler:",
            "Schedule type:",
            "CFG scale:",
            "Seed:",
            "Size:",
            "Model hash:",
            "Model:",
            "Clip skip:",
            "Version:"
        };

        /// <summary>
        /// WebUI形式のメタデータを解析する
        /// </summary>
        public StableDiffusionMetadata Parse(string metadataText)
        {
            var metadata = new StableDiffusionMetadata
            {
                RawMetadata = metadataText,
                Generator = "WebUI",
            };

            try
            {
                if (string.IsNullOrWhiteSpace(metadataText))
                {
                    return metadata;
                }

                // 行に分割
                var lines = metadataText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                // セクションに分割
                var sections = SplitIntoSections(lines);

                // プロンプトの解析
                if (sections.TryGetValue("Prompt", out var promptText))
                {
                    metadata.PositivePrompt = promptText;
                }

                // Negative Promptの解析
                if (sections.TryGetValue("Negative prompt", out var negativePromptText))
                {
                    metadata.NegativePrompt = negativePromptText;
                }

                // パラメータセクションからモデル情報を抽出
                if (sections.TryGetValue("Parameters", out var parameters))
                {
                    var paramLines = parameters.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in paramLines)
                    {
                        if (line.Contains("Model:", StringComparison.OrdinalIgnoreCase))
                        {
                            var modelMatch = Regex.Match(line, @"Model:\s*([^,]+)", RegexOptions.IgnoreCase);
                            if (modelMatch.Success)
                            {
                                metadata.ModelName = modelMatch.Groups[1].Value.Trim();
                            }
                        }
                    }
                }

                metadata.ParseSuccess = true;
            }
            catch (Exception)
            {
                metadata.ParseSuccess = false;
            }

            return metadata;
        }

        /// <summary>
        /// このパーサーでメタデータを解析可能かどうかを判定する
        /// </summary>
        public bool CanParse(string metadataText)
        {
            if (string.IsNullOrWhiteSpace(metadataText))
                return false;

            // WebUI形式の特徴的なパターンを確認
            // 1. Negative promptセクションがあるか
            // 2. Stepsセクションがあるか
            return metadataText.Contains("Negative prompt:", StringComparison.OrdinalIgnoreCase) ||
                   metadataText.Contains("Steps:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// テキストをセクションに分割する
        /// </summary>
        private Dictionary<string, string> SplitIntoSections(string[] lines)
        {
            var sections = new Dictionary<string, string>();
            var currentSection = new StringBuilder();
            string currentSectionName = "Prompt";
            bool isInParametersSection = false;

            // パラメータセクションのコンテンツを一時的に保持
            var parametersContent = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine)) continue;

                // パラメータセクション内の場合
                if (isInParametersSection)
                {
                    parametersContent.AppendLine(trimmedLine);
                    continue;
                }

                var isNewSection = false;

                // 既定のセクション開始をチェック
                foreach (var pattern in SectionPatterns)
                {
                    if (trimmedLine.StartsWith(pattern.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        // 現在のセクションを保存
                        if (currentSection.Length > 0)
                        {
                            sections[currentSectionName] = currentSection.ToString().Trim();
                            currentSection.Clear();
                        }

                        currentSectionName = pattern.Value;
                        var content = trimmedLine.Substring(pattern.Key.Length).Trim();

                        // Stepsから始まるパラメータセクションの場合
                        if (pattern.Key == "Steps:")
                        {
                            isInParametersSection = true;
                            parametersContent.AppendLine(trimmedLine);
                        }
                        else if (!string.IsNullOrWhiteSpace(content))
                        {
                            currentSection.AppendLine(content);
                        }

                        isNewSection = true;
                        break;
                    }
                }

                // パラメータセクションの開始をチェック
                if (!isNewSection && Array.Exists(ParameterKeys, key => trimmedLine.StartsWith(key, StringComparison.OrdinalIgnoreCase)))
                {
                    // 現在のセクションを保存
                    if (currentSection.Length > 0)
                    {
                        sections[currentSectionName] = currentSection.ToString().Trim();
                        currentSection.Clear();
                    }

                    isInParametersSection = true;
                    currentSectionName = "Parameters";
                    parametersContent.AppendLine(trimmedLine);
                    continue;
                }

                // 通常のコンテンツ行の処理
                if (!isNewSection && !isInParametersSection)
                {
                    currentSection.AppendLine(trimmedLine);
                }
            }

            // 最後のセクションを保存
            if (currentSection.Length > 0)
            {
                sections[currentSectionName] = currentSection.ToString().Trim();
            }

            // パラメータセクションを保存
            if (parametersContent.Length > 0)
            {
                sections["Parameters"] = parametersContent.ToString().Trim();
            }

            return sections;
        }

        /// <summary>
        /// テキストからLoRAタグを抽出する
        /// </summary>
        /// <param name="text">対象テキスト</param>
        /// <returns>抽出されたLoRAタグリスト</returns>
        public List<string> ExtractLoras(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            try
            {
                // LoRAタグを抽出
                var loraRegex = new Regex(@"<lora:[^>]+>");
                var loraMatches = loraRegex.Matches(text);
                return loraMatches
                    .Cast<Match>()
                    .Where(m => !string.IsNullOrWhiteSpace(m.Value))
                    .Select(m => m.Value)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// テキストからタグを抽出する
        /// </summary>
        /// <param name="text">対象テキスト</param>
        /// <returns>抽出されたタグリスト</returns>
        public List<string> ExtractTags(string text)
        {
            return ParseTags(text);
        }

        /// <summary>
        /// タグを解析する共通メソッド
        /// </summary>
        private static List<string> ParseTags(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            // LoRAタグを一時的に置換して保護
            var loraRegex = new Regex(@"<(?:lora|Lora|LORA):[^>]+>");
            var loraPlaceholders = new Dictionary<string, string>();
            var counter = 0;

            string processedText = loraRegex.Replace(text, match =>
            {
                var placeholder = $"@@LORA_PLACEHOLDER_{counter++}@@";
                loraPlaceholders[placeholder] = match.Value;
                return $",{placeholder},"; // LoRAタグの前後にカンマを追加
            });

            // 括弧内のカンマを一時的に置換
            processedText = Regex.Replace(processedText, @"\(([^)]*)\)", m => m.Value.Replace(",", "@@COMMA@@"));

            // カンマで分割して処理
            var tags = processedText.Split(',')
                .Select(tag => tag.Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag =>
                {
                    // LoRAプレースホルダーを元に戻す
                    if (tag.StartsWith("@@LORA_PLACEHOLDER_") && tag.EndsWith("@@"))
                    {
                        return loraPlaceholders.TryGetValue(tag, out var loraTag) ? loraTag : tag;
                    }
                    // カンマを元に戻す
                    return tag.Replace("@@COMMA@@", ",");
                })
                .ToList();

            return tags;
        }
    }
}