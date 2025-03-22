using System;
using System.IO;
using Hjg.Pngcs;
using Hjg.Pngcs.Chunks;
using System.Collections.Generic;
using StableDiffusionTools;

namespace Illustra.Helpers
{
    /// <summary>
    /// PNG画像のメタデータを読み取るためのヘルパークラス
    /// </summary>
    public class PngMetadataReader
    {
        /// <summary>
        /// Stable Diffusionで使用される一般的なメタデータキーとソースのマッピング
        /// </summary>
        public static readonly Dictionary<string, string> MetadataKeySourceMap = new()
        {
            { "parameters", "WebUI" },  // WebUI / A1111
            { "prompt", "ComfyUI" },    // ComfyUI
            { "sd-metadata", "Unknown" } // その他の実装用
        };

        /// <summary>
        /// Stable Diffusionで使用される一般的なメタデータキー
        /// </summary>
        public static readonly string[] KnownStableDiffusionKeys = new[]
        {
            "parameters", // WebUI / A1111
            "prompt",     // ComfyUI
            "sd-metadata" // その他の実装用
        };

        /// <summary>
        /// PNGファイルから指定したtEXtチャンクのデータを返す
        /// </summary>
        /// <param name="filePath">対象PNGファイルパス</param>
        /// <param name="chunkKey">取得したいチャンク名 (例: "parameters")</param>
        /// <returns>チャンクのデータ文字列。なければnull</returns>
        public static string ReadTextChunk(string filePath, string chunkKey)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("PNG file not found.", filePath);

            try
            {
                using var stream = File.OpenRead(filePath);
                var reader = new PngReader(stream);
                try
                {
                    var metadata = reader.GetMetadata();
                    var textChunks = metadata.GetTxtsForKey(chunkKey);
                    if (textChunks.Count > 0)
                        return textChunks[0].GetVal();
                }
                finally
                {
                    reader.End(); // 読み終え
                    stream.Dispose(); // 明示的にストリームを閉じる
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading PNG metadata: {ex.Message}");
                return null;
            }

            return null; // なければnull
        }

        /// <summary>
        /// ファイルがPNG形式かどうかを判定する
        /// </summary>
        private static bool IsPng(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            return Path.GetExtension(filePath).Equals(".png", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// PNGファイルから既知のStable Diffusionメタデータを抽出する
        /// </summary>
        /// <param name="filePath">対象PNGファイルパス</param>
        /// <returns>メタデータオブジェクト。メタデータがない場合はHasMetadata=falseのインスタンス</returns>
        public static StableDiffusionMetadata ExtractStableDiffusionMetadata(string filePath)
        {
            if (!IsPng(filePath))
                return new StableDiffusionMetadata();

            // 既知のキーを順番に試す
            foreach (var key in KnownStableDiffusionKeys)
            {
                var metadata = ReadTextChunk(filePath, key);
                if (!string.IsNullOrEmpty(metadata))
                {
                    // どのキーから取得したかを記録（Dictionary を使用）
                    string source = MetadataKeySourceMap.TryGetValue(key, out var sourceValue)
                        ? sourceValue
                        : "Unknown";

                    return StableDiffusionMetadataManager.ParseMetadataText(metadata, source);
                }
            }

            return new StableDiffusionMetadata();
        }
    }
}
