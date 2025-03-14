using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StableDiffusionTools
{
    /// <summary>
    /// Stable Diffusionの出力テキストを解析するためのクラス
    /// </summary>
    public class StableDiffusionParser
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
        /// タグを解析する共通メソッド
        /// </summary>
        private static List<string> ParseTags(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            // 括弧内のカンマを一時的に置換
            var tempText = Regex.Replace(text, @"\(([^)]*)\)", m => m.Value.Replace(",", "@@COMMA@@"));

            // カンマで分割して処理
            return tempText.Split(',')
                .Select(tag => tag.Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Replace("@@COMMA@@", ","))
                .ToList();
        }

        /// <summary>
        /// テキストをセクションに分割する
        /// </summary>
        private static Dictionary<string, string> SplitIntoSections(string[] lines)
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
        /// Stable Diffusionのパース結果を保持するクラス
        /// </summary>
        public class ParseResult
        {
            /// <summary>
            /// 元のプロンプト全体
            /// </summary>
            public string Prompt { get; set; } = string.Empty;

            /// <summary>
            /// 抽出されたプロンプトタグのリスト
            /// </summary>
            public List<string> Tags { get; set; } = new List<string>();

            /// <summary>
            /// 抽出されたLoRAタグのリスト
            /// </summary>
            public List<string> Loras { get; set; } = new List<string>();

            /// <summary>
            /// Negative Prompt全体
            /// </summary>
            public string NegativePrompt { get; set; } = string.Empty;

            /// <summary>
            /// 抽出されたネガティブプロンプトタグのリスト
            /// </summary>
            public List<string> NegativeTags { get; set; } = new List<string>();

            /// <summary>
            /// モデル名
            /// </summary>
            public string Model { get; set; } = string.Empty;

            /// <summary>
            /// モデルハッシュ
            /// </summary>
            public string ModelHash { get; set; } = string.Empty;
        }

        /// <summary>
        /// Stable Diffusionの出力テキストを解析する
        /// </summary>
        /// <param name="text">解析するテキスト</param>
        /// <returns>解析結果</returns>
        public static ParseResult Parse(string? text)
        {
            var result = new ParseResult();

            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return result;
                }

                // 行に分割
                var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                // セクションに分割
                var sections = SplitIntoSections(lines);

                // プロンプトの解析
                if (sections.TryGetValue("Prompt", out var promptText))
                {
                    result.Prompt = promptText;

                    try
                    {
                        // LoRAタグを抽出
                        var loraRegex = new Regex(@"<lora:[^>]+>");
                        var loraMatches = loraRegex.Matches(promptText);
                        result.Loras = loraMatches
                            .Cast<Match>()
                            .Where(m => !string.IsNullOrWhiteSpace(m.Value))
                            .Select(m => m.Value)
                            .ToList();

                        // LoRAタグを除いてタグを抽出
                        string textWithoutLoras = loraRegex.Replace(promptText, "");
                        result.Tags = ParseTags(textWithoutLoras);
                    }
                    catch (Exception)
                    {
                        result.Tags = new List<string>();
                        result.Loras = new List<string>();
                    }
                }

                // Negative Promptの解析
                if (sections.TryGetValue("Negative prompt", out var negativePromptText))
                {
                    result.NegativePrompt = negativePromptText;
                    try
                    {
                        result.NegativeTags = ParseTags(negativePromptText);
                    }
                    catch (Exception)
                    {
                        result.NegativeTags = new List<string>();
                    }
                }

                // パラメータセクションからモデル情報を抽出
                if (sections.TryGetValue("Parameters", out var parameters))
                {
                    var paramLines = parameters.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in paramLines)
                    {
                        if (line.Contains("Model:", StringComparison.OrdinalIgnoreCase) &&
                            !line.Contains("Model hash:", StringComparison.OrdinalIgnoreCase))
                        {
                            var modelMatch = Regex.Match(line, @"Model:\s*([^,]+)", RegexOptions.IgnoreCase);
                            if (modelMatch.Success)
                            {
                                result.Model = modelMatch.Groups[1].Value.Trim();
                            }
                        }
                        else if (line.Contains("Model hash:", StringComparison.OrdinalIgnoreCase))
                        {
                            var hashMatch = Regex.Match(line, @"Model hash:\s*([^,]+)", RegexOptions.IgnoreCase);
                            if (hashMatch.Success)
                            {
                                result.ModelHash = hashMatch.Groups[1].Value.Trim();
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 解析に失敗した場合はデフォルトの結果を返す
            }

            return result;
        }
    }
}
