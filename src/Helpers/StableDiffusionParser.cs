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
                // 入力値の検証
                if (string.IsNullOrWhiteSpace(text))
                {
                    return result;
                }

                // 行ごとに分割
                string[] lines;
                try
                {
                    lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                }
                catch (Exception)
                {
                    // 分割に失敗した場合は単一行として扱う
                    lines = new[] { text };
                }

                // Negative promptを見つける（区切りとして使用）
                int negativePromptIndex = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    try
                    {
                        if (lines[i].Trim().StartsWith("Negative prompt:", StringComparison.OrdinalIgnoreCase))
                        {
                            negativePromptIndex = i;
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        // 個別の行の処理でエラーが発生しても続行
                        continue;
                    }
                }

                // プロンプトの解析
                try
                {
                    // Negative promptより前の行を取得（プロンプトとタグ情報）
                    if (negativePromptIndex > 0)
                    {
                        var promptBuilder = new StringBuilder();
                        for (int i = 0; i < negativePromptIndex; i++)
                        {
                            try
                            {
                                promptBuilder.AppendLine(lines[i]);
                            }
                            catch (Exception)
                            {
                                // 個別の行の追加でエラーが発生しても続行
                                continue;
                            }
                        }

                        // プロンプト全体を保存
                        result.Prompt = promptBuilder.ToString().Trim();

                        try
                        {
                            // LoRAタグを正規表現で抽出
                            var loraRegex = new Regex(@"<lora:[^>]+>");
                            var loraMatches = loraRegex.Matches(result.Prompt);
                            foreach (Match match in loraMatches)
                            {
                                if (!string.IsNullOrWhiteSpace(match.Value))
                                {
                                    result.Loras.Add(match.Value);
                                }
                            }

                            // LoRAタグを除いたテキストからタグを抽出
                            string textWithoutLoras = loraRegex.Replace(result.Prompt, "");
                            var tags = textWithoutLoras.Split(',')
                                .Select(tag => tag.Trim())
                                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                                .ToList();

                            result.Tags = tags;
                        }
                        catch (Exception)
                        {
                            // タグ抽出に失敗した場合は空のリストを維持
                            result.Tags = new List<string>();
                            result.Loras = new List<string>();
                        }
                    }
                }
                catch (Exception)
                {
                    // プロンプト解析全体が失敗した場合は空の状態を維持
                }

                // Negative Promptの解析
                try
                {
                    if (negativePromptIndex != -1 && negativePromptIndex < lines.Length)
                    {
                        // Negative promptの行を取得
                        string negativePromptLine = lines[negativePromptIndex];

                        // "Negative prompt:" の部分を削除
                        string negativePromptText = negativePromptLine.Replace("Negative prompt:", "").Trim();

                        // Negative promptの次の行がモデル情報などを含む行かチェック
                        int nextLineIndex = negativePromptIndex + 1;
                        bool isNextLineParameter = nextLineIndex < lines.Length &&
                                                (lines[nextLineIndex].Contains(":") &&
                                                !lines[nextLineIndex].StartsWith("Negative prompt:", StringComparison.OrdinalIgnoreCase));

                        // Negative prompt全体を取得
                        string fullNegativePrompt = negativePromptText;

                        // もし次の行がパラメータ行でない場合は、Negative promptの一部と見なす
                        if (!isNextLineParameter && nextLineIndex < lines.Length)
                        {
                            try
                            {
                                fullNegativePrompt += Environment.NewLine + lines[nextLineIndex];
                            }
                            catch (Exception)
                            {
                                // 次の行の追加に失敗した場合は無視
                            }
                        }

                        result.NegativePrompt = fullNegativePrompt;

                        try
                        {
                            // Negative promptをタグに分割
                            var negativeTags = fullNegativePrompt.Split(',')
                                .Select(tag => tag.Trim())
                                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                                .ToList();

                            result.NegativeTags = negativeTags;
                        }
                        catch (Exception)
                        {
                            // タグ分割に失敗した場合は空のリストを維持
                            result.NegativeTags = new List<string>();
                        }
                    }
                }
                catch (Exception)
                {
                    // Negative Prompt解析全体が失敗した場合は空の状態を維持
                }

                // モデル情報の抽出
                try
                {
                    foreach (var line in lines)
                    {
                        try
                        {
                            if (line.Contains("Model:"))
                            {
                                var match = Regex.Match(line, @"Model:\s*([^,]+)");
                                if (match.Success && match.Groups.Count > 1)
                                {
                                    result.Model = match.Groups[1].Value.Trim();
                                }
                            }
                            else if (line.Contains("Model hash:"))
                            {
                                var match = Regex.Match(line, @"Model hash:\s*([^,]+)");
                                if (match.Success && match.Groups.Count > 1)
                                {
                                    result.ModelHash = match.Groups[1].Value.Trim();
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // 個別の行の解析エラーは無視して続行
                            continue;
                        }
                    }
                }
                catch (Exception)
                {
                    // モデル情報の抽出全体が失敗した場合は空の状態を維持
                }
            }
            catch (Exception)
            {
                // 最上位での例外をキャッチし、デフォルトの結果を返す
            }

            return result;
        }
    }
}
