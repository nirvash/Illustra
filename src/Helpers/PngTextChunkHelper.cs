using System;
using System.IO;
using Hjg.Pngcs;
using Hjg.Pngcs.Chunks;

namespace Illustra.Helpers
{
    public class PngMetadataReader
    {
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
                throw new Exception($"Error reading PNG metadata: {ex.Message}", ex);
            }

            return null; // なければnull
        }
    }
}
