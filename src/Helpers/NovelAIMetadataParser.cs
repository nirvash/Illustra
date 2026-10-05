using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Illustra.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace Illustra.Helpers
{
    /// <summary>NovelAI PNG のテキストメタデータと alpha LSB stealth_pngcomp を読み取る。</summary>
    public static class NovelAIMetadataParser
    {
        private const int MaxPngBytes = 128 * 1024 * 1024;
        private const int MaxImagePixels = 32 * 1024 * 1024;
        private const int MaxCompressedBytes = 4 * 1024 * 1024;
        private const int MaxExpandedBytes = 4 * 1024 * 1024;
        private const string Magic = "stealth_pngcomp";

        /// <summary>通常チャンクが認識した NovelAI メタデータを解析する。</summary>
        public static GenerationMetadata ParseTextTags(IReadOnlyDictionary<string, string> tags)
        {
            if (tags == null) return null;
            tags.TryGetValue("Description", out var description);
            tags.TryGetValue("Software", out var software);
            tags.TryGetValue("Source", out var source);
            tags.TryGetValue("Comment", out var commentText);

            JsonDocument comment = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(commentText)) comment = JsonDocument.Parse(commentText);
                var commentRoot = comment?.RootElement;
                if (!IsNovelAI(software, source, commentRoot) ||
                    (string.IsNullOrWhiteSpace(description) && commentRoot == null)) return null;

                return BuildMetadata(description, software, source, commentText, commentRoot);
            }
            catch (JsonException)
            {
                // Description/Software/Source だけで識別できる画像は、不正 Comment があっても表示する。
                if (string.IsNullOrWhiteSpace(description) || !IsNovelAI(software, source, null)) return null;
                return BuildMetadata(description, software, source, commentText, null);
            }
            finally { comment?.Dispose(); }
        }

        /// <summary>alpha LSB に埋め込まれた公式 stealth_pngcomp 形式を安全に読む。</summary>
        public static GenerationMetadata ParseStealthPng(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length < 33 || info.Length > MaxPngBytes) return null;
                using var stream = File.OpenRead(filePath);
                Span<byte> header = stackalloc byte[29];
                stream.ReadExactly(header);
                if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                    Encoding.ASCII.GetString(header[12..16]) != "IHDR" ||
                    header[24] != 8 || header[25] != 6) return null; // 8-bit RGBA のみ
                uint width = ReadBE32(header[16..20]);
                uint height = ReadBE32(header[20..24]);
                if (width == 0 || height == 0 || (ulong)width * height > MaxImagePixels) return null;

                using var image = LoadFirstFrame(filePath);
                ulong availableBits = (ulong)width * height;
                ulong magicBits = (ulong)Encoding.UTF8.GetByteCount(Magic) * 8;
                if (availableBits < magicBits + 32) return null;
                var reader = new AlphaBitReader(image, width, height);
                byte[] magic = reader.ReadBytes(Encoding.UTF8.GetByteCount(Magic));
                if (Encoding.UTF8.GetString(magic) != Magic) return null;
                uint bitLength = ReadBE32(reader.ReadBytes(4));
                if ((bitLength & 7) != 0) return null;
                uint byteLength = bitLength / 8;
                if (byteLength == 0 || byteLength > MaxCompressedBytes ||
                    (ulong)byteLength * 8 > reader.RemainingBits) return null;

                byte[] compressed = reader.ReadBytes((int)byteLength);
                using var input = new MemoryStream(compressed, false);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                var buffer = new byte[8192];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (output.Length + read > MaxExpandedBytes) return null;
                    output.Write(buffer, 0, read);
                }
                using var document = JsonDocument.Parse(output.ToArray());
                if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
                string raw = document.RootElement.GetRawText();
                JsonDocument normalizedComment = null;
                try
                {
                    string comment = null;
                    if (document.RootElement.TryGetProperty("Comment", out var commentElement))
                    {
                        comment = commentElement.ValueKind == JsonValueKind.String
                            ? commentElement.GetString()
                            : commentElement.GetRawText();
                        if (!string.IsNullOrWhiteSpace(comment))
                        {
                            try { normalizedComment = JsonDocument.Parse(comment); } catch (JsonException) { }
                        }
                    }
                    if (!IsNovelAI(GetString(document.RootElement, "Software"),
                            GetString(document.RootElement, "Source"), document.RootElement) &&
                        (normalizedComment == null || !IsNovelAI(null, null, normalizedComment.RootElement)))
                        return null;
                    return BuildMetadata(GetString(document.RootElement, "Description"),
                        GetString(document.RootElement, "Software"), GetString(document.RootElement, "Source"),
                        comment, normalizedComment?.RootElement, raw);
                }
                finally { normalizedComment?.Dispose(); }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                      ex is InvalidDataException || ex is JsonException ||
                                      ex is ArgumentException || ex is NotSupportedException ||
                                      ex is SixLabors.ImageSharp.ImageFormatException || ex is OverflowException)
            {
                return null;
            }
        }

        private static Image<Rgba32> LoadFirstFrame(string filePath) =>
            Image.Load<Rgba32>(new DecoderOptions { MaxFrames = 1 }, filePath);

        private static GenerationMetadata BuildMetadata(string description, string software, string source,
            string commentText, JsonElement? comment, string raw = null)
        {
            string positive = !string.IsNullOrWhiteSpace(description) ? description :
                comment.HasValue ? GetString(comment.Value, "prompt") : null;
            if (string.IsNullOrWhiteSpace(positive) && comment.HasValue)
            {
                // V4 base_caption が文字列なら通常 prompt 欄へ。構造体なら RawMetadata にそのまま保持。
                positive = GetString(comment.Value, "base_caption");
            }

            var metadata = new GenerationMetadata
            {
                Generator = "NovelAI",
                ModelName = source ?? string.Empty,
                Prompt = positive ?? string.Empty,
                RawMetadata = raw ?? BuildRaw(description, software, source, commentText)
            };
            if (comment.HasValue && comment.Value.ValueKind == JsonValueKind.Object)
            {
                var root = comment.Value;
                if (root.TryGetProperty("uc", out var uc)) metadata.NegativePrompt = ScalarText(uc);
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Name is not ("steps" or "sampler" or "seed" or "scale")) continue;
                    metadata.Parameters[property.Name] = ScalarText(property.Value);
                }
            }
            metadata.ParseSuccess = !string.IsNullOrWhiteSpace(metadata.Prompt) ||
                                    !string.IsNullOrWhiteSpace(metadata.NegativePrompt) || metadata.Parameters.Count > 0;
            return metadata;
        }

        private static string BuildRaw(string description, string software, string source, string comment)
        {
            var parts = new List<string>();
            if (description != null) parts.Add("Description: " + description);
            if (software != null) parts.Add("Software: " + software);
            if (source != null) parts.Add("Source: " + source);
            if (comment != null) parts.Add("Comment: " + comment);
            return string.Join("\n", parts);
        }

        private static bool IsNovelAI(string software, string source, JsonElement? comment)
        {
            if (software?.Contains("novelai", StringComparison.OrdinalIgnoreCase) == true ||
                source?.Contains("novelai", StringComparison.OrdinalIgnoreCase) == true) return true;
            if (!comment.HasValue || comment.Value.ValueKind != JsonValueKind.Object) return false;
            var root = comment.Value;
            return root.TryGetProperty("uc", out _) &&
                (root.TryGetProperty("steps", out _) || root.TryGetProperty("sampler", out _) ||
                 root.TryGetProperty("scale", out _) || root.TryGetProperty("base_caption", out _) ||
                 root.TryGetProperty("char_captions", out _));
        }

        private static string GetString(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static string ScalarText(JsonElement element) => element.ValueKind == JsonValueKind.String
            ? element.GetString() : element.GetRawText();

        private static uint ReadBE32(ReadOnlySpan<byte> bytes) =>
            ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

        private sealed class AlphaBitReader
        {
            private readonly Image<Rgba32> _image;
            private readonly uint _width, _height;
            private ulong _position;
            public ulong RemainingBits => (ulong)_width * _height - _position;
            public AlphaBitReader(Image<Rgba32> image, uint width, uint height)
            { _image = image; _width = width; _height = height; }
            public byte[] ReadBytes(int count)
            {
                if ((ulong)count * 8 > RemainingBits) throw new InvalidDataException("LSB data exceeds image capacity");
                var result = new byte[count];
                for (int i = 0; i < count; i++)
                    for (int bit = 7; bit >= 0; bit--)
                    {
                        ulong index = _position++;
                        int x = (int)(index / _height), y = (int)(index % _height);
                        result[i] |= (byte)((_image[x, y].A & 1) << bit);
                    }
                return result;
            }
        }
    }
}
