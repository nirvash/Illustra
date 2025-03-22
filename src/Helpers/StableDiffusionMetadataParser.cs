using System;
using System.Text.Json.Nodes;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Illustra.Helpers;

namespace StableDiffusionTools
{
    /// <summary>
    /// Stable Diffusionメタデータのパーサーインターフェース
    /// </summary>
    public interface IStableDiffusionMetadataParser
    {
        /// <summary>
        /// メタデータ文字列を解析してメタデータオブジェクトを返す
        /// </summary>
        StableDiffusionMetadata Parse(string metadataText);

        /// <summary>
        /// このパーサーで解析可能かどうかを判定する
        /// </summary>
        bool CanParse(string metadataText);
    }

    /// <summary>
    /// メタデータ解析を行うマネージャークラス
    /// </summary>
    public static class StableDiffusionMetadataManager
    {
        /// <summary>
        /// ファイルからStable Diffusionのメタデータを抽出する
        /// </summary>
        /// <param name="filePath">画像ファイルパス</param>
        /// <returns>抽出したメタデータ。メタデータがない場合はHasMetadata=falseのインスタンス</returns>
        public static async Task<StableDiffusionMetadata> ExtractMetadataFromFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new StableDiffusionMetadata();
            }

            string fileExtension = Path.GetExtension(filePath).ToLowerInvariant();

            // PNGファイルの場合、PngMetadataReaderの拡張機能を使用
            if (fileExtension == ".png")
            {
                try
                {
                    var metadata = PngMetadataReader.ExtractStableDiffusionMetadata(filePath);
                    if (metadata.HasMetadata)
                    {
                        return metadata;
                    }
                }
                catch
                {
                    // PNGメタデータの読み取りエラーは無視して続行
                }
            }

            // EXIFからUserCommentを抽出（主にJPEG用）
            try
            {
                var exifMetadata = await Task.Run(() => ExtractExifUserComment(filePath));
                if (!string.IsNullOrEmpty(exifMetadata))
                {
                    return ParseMetadataText(exifMetadata, "WebUI"); // 現状ではExifはWebUIのみ対応
                }
            }
            catch
            {
                // EXIFの読み取りに失敗した場合は無視
            }

            // メタデータを見つけられなかった場合
            return new StableDiffusionMetadata();
        }

        /// <summary>
        /// UTF-16エンコードされたバイト配列をデコードする
        /// </summary>
        public static string DecodeUtf16(byte[] data)
        {
            Encoding encoding;

            // BOMでエンディアンを判定
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            {
                encoding = Encoding.BigEndianUnicode;
                data = data.Skip(2).ToArray(); // BOMを除去
            }
            else if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            {
                encoding = Encoding.Unicode; // UTF-16LE
                data = data.Skip(2).ToArray(); // BOMを除去
            }
            else
            {
                // BOMがない場合、Exifの仕様に従いUTF-16BEとみなす
                encoding = Encoding.BigEndianUnicode;
            }

            return encoding.GetString(data);
        }

        /// <summary>
        /// EXIFからUserCommentを抽出する
        /// </summary>
        private static string ExtractExifUserComment(string filePath)
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(filePath);
                var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

                if (exif != null)
                {
                    try
                    {
                        var bytes = exif.GetByteArray(ExifDirectoryBase.TagUserComment);
                        if (bytes != null && bytes.Length > 8)
                        {
                            // 最初の8バイトがエンコーディング識別子
                            var encodingStr = Encoding.ASCII.GetString(bytes.Take(8).ToArray());
                            if (encodingStr.StartsWith("ASCII") || encodingStr.Equals("\0\0\0\0\0\0\0\0"))
                            {
                                // ASCIIエンコーディング情報がある場合はUTF-8としてデコード
                                return Encoding.UTF8.GetString(bytes.Skip(8).ToArray());
                            }
                            else if (encodingStr.StartsWith("UNICODE"))
                            {
                                // UNICODEエンコーディング情報がある場合はUTF-16としてデコード
                                return DecodeUtf16(bytes.Skip(8).ToArray());
                            }
                        }

                        // その他の場合は既存の方式で取得
                        return exif.GetDescription(ExifDirectoryBase.TagUserComment);
                    }
                    catch
                    {
                        return exif.GetDescription(ExifDirectoryBase.TagUserComment);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exif読み取りエラー: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// メタデータのテキストを解析して適切なパーサーを使用
        /// </summary>
        public static StableDiffusionMetadata ParseMetadataText(string metadataText, string source = "")
        {
            if (string.IsNullOrEmpty(metadataText))
            {
                return new StableDiffusionMetadata();
            }

            // ComfyUI形式かどうか判定（JSONかどうかで簡易判断）
            if (IsLikelyComfyUIFormat(metadataText))
            {
                var parser = new ComfyUIMetadataParser();
                if (parser.CanParse(metadataText))
                {
                    var result = parser.Parse(metadataText);
                    result.Generator = "ComfyUI";
                    return result;
                }
            }

            // WebUI形式のパース
            var webUIParser = new WebUIMetadataParser();
            if (webUIParser.CanParse(metadataText))
            {
                var result = webUIParser.Parse(metadataText);
                result.Generator = "WebUI";
                return result;
            }

            // どのパーサーでも解析できなかった場合
            return new StableDiffusionMetadata
            {
                RawMetadata = metadataText,
                ParseSuccess = false
            };
        }

        /// <summary>
        /// ComfyUI形式のJSONらしいかどうかを簡易判定
        /// </summary>
        private static bool IsLikelyComfyUIFormat(string text)
        {
            text = text.Trim();
            return text.StartsWith("{") && text.EndsWith("}");
        }

        /// <summary>
        /// StableDiffusionMetadataをStableDiffusionParser.ParseResultに変換する
        /// </summary>
        /// <param name="metadata">StableDiffusionMetadata</param>
        /// <returns>変換されたParseResult</returns>
        public static StableDiffusionParser.ParseResult ConvertToLegacyParseResult(StableDiffusionMetadata metadata)
        {
            if (metadata == null || !metadata.HasMetadata)
            {
                return new StableDiffusionParser.ParseResult();
            }

            var legacyResult = new StableDiffusionParser.ParseResult
            {
                Prompt = metadata.PositivePrompt,
                NegativePrompt = metadata.NegativePrompt,
                Model = metadata.ModelName,
                ModelHash = metadata.ModelHash
            };

            // ModelLorasからLorasに追加
            if (metadata.ModelLoras != null && metadata.ModelLoras.Any())
            {
                if (legacyResult.Loras == null)
                {
                    legacyResult.Loras = new List<string>();
                }

                foreach (var lora in metadata.ModelLoras)
                {
                    if (!string.IsNullOrEmpty(lora) && !legacyResult.Loras.Contains(lora))
                    {
                        legacyResult.Loras.Add(lora);
                    }
                }
            }

            // WebUIパーサーを使用してタグとLoRAを抽出
            try
            {
                var webUIParser = new WebUIMetadataParser();

                // ポジティブプロンプトからタグとLoRAを抽出
                if (!string.IsNullOrEmpty(metadata.PositivePrompt))
                {
                    // LoRAタグを抽出して既存のリストにマージ
                    var extractedLoras = webUIParser.ExtractLoras(metadata.PositivePrompt);
                    if (extractedLoras.Any())
                    {
                        if (legacyResult.Loras == null)
                        {
                            legacyResult.Loras = new List<string>();
                        }

                        foreach (var lora in extractedLoras)
                        {
                            if (!legacyResult.Loras.Contains(lora))
                            {
                                legacyResult.Loras.Add(lora);
                            }
                        }
                    }

                    legacyResult.Tags = webUIParser.ExtractTags(metadata.PositivePrompt);
                }

                // ネガティブプロンプトからタグを抽出
                if (!string.IsNullOrEmpty(metadata.NegativePrompt))
                {
                    legacyResult.NegativeTags = webUIParser.ExtractTags(metadata.NegativePrompt);
                }
            }
            catch
            {
                // タグ抽出に失敗しても続行
            }

            return legacyResult;
        }
    }
}