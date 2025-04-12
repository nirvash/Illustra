using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows;

namespace Illustra.Helpers
{
    /// <summary>
    /// ドラッグ＆ドロップされた画像ファイルの保存を支援するヘルパークラス
    /// </summary>
    public class ImageDropHelper
    {
        /// <summary>
        /// ドラッグ＆ドロップされたデータの各フォーマットの内容（先頭500文字）をログに出力します
        /// </summary>
        /// <param name="dataObject">解析するドラッグ＆ドロップデータ</param>
        private void LogDataObjectFormats(IDataObject dataObject)
        {
            string[] formats = new[]
            {
                "FileContents",
                "text/x-moz-url",
                "UniformResourceLocatorW",
                "UniformResourceLocator",
                "Text",
                "UnicodeText",
                "System.String",
                "HTML Format",
                "text/html"
            };

            foreach (var format in formats)
            {
                try
                {
                    if (dataObject.GetDataPresent(format))
                    {
                        var data = dataObject.GetData(format);
                        string textData;

                        if (data is byte[] bytes)
                        {
                            textData = Encoding.UTF8.GetString(bytes);
                        }
                        else if (data is MemoryStream ms)
                        {
                            var buffer = new byte[ms.Length];
                            ms.Position = 0;
                            ms.Read(buffer, 0, buffer.Length);
                            textData = Encoding.UTF8.GetString(buffer);
                        }
                        else
                        {
                            textData = data?.ToString() ?? string.Empty;
                        }

                        if (textData.Length > 500)
                        {
                            textData = textData.Substring(0, 500);
                        }
                        LogHelper.LogAnalysis($"[ANALYSIS] Format: {format}\nContent: {textData}");
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogError($"[ANALYSIS] フォーマット {format} の取得に失敗: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// IDataObject から画像ファイルを保存し、保存されたファイルパスのリストを返します
        /// </summary>
        /// <param name="dataObject">ドロップされたデータ</param>
        /// <param name="targetFolderPath">保存先フォルダパス</param>
        /// <returns>保存された画像ファイルのパスのリスト</returns>
        public async Task<List<string>> ProcessImageDrop(IDataObject dataObject, string targetFolderPath)
        {
            // フォーマット情報のログ出力
            // LogDataObjectFormats(dataObject);

            if (string.IsNullOrEmpty(targetFolderPath))
                return new List<string>();

            List<string> fileList = new List<string>();

            bool hasDescriptor = dataObject.GetDataPresent("FileGroupDescriptorW");
            bool hasContents = dataObject.GetDataPresent("FileContents");
            bool hasIgnoreFlag = dataObject.GetDataPresent("chromium/x-ignore-file-contents");

            // 仮想ファイル処理（ちゃんと中身がある場合）
            if (hasDescriptor && hasContents && !hasIgnoreFlag &&
                TryGetValidVirtualFile(dataObject, out var stream, out var fileName))
            {
                try
                {
                    if (stream != null)
                    {
                        string tempPath = Path.Combine(Path.GetTempPath(), fileName);
                        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                        {
                            stream.CopyTo(fs);
                        }
                        fileList.Add(tempPath);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogError("[仮想ファイル] 処理失敗: " + ex.Message);
                }
                finally
                {
                    stream?.Dispose();
                }
            }
            // ローカルファイル（FileDrop）
            else if (!hasIgnoreFlag && dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                try
                {
                    string[] files = (string[])dataObject.GetData(DataFormats.FileDrop);
                    fileList.AddRange(files);
                }
                catch (COMException ex)
                {
                    LogHelper.LogError("[FileDrop] 例外: " + ex.Message);
                }
            }
            // HTML形式 (Twitterからのドロップなど)
            else if (dataObject.GetDataPresent(DataFormats.Html))
            {
                try
                {
                    string html = dataObject.GetData(DataFormats.Html) as string;
                    if (!string.IsNullOrEmpty(html))
                    {
                        // <img src="..."> から src 属性のURLを抽出
                        var match = Regex.Match(html, @"<img[^>]+src=[""'](?<url>https?://[^""']+)[""']");
                        if (match.Success)
                        {
                            string rawImageUrl = match.Groups["url"].Value;
                            // HTMLエンティティをデコード (&amp; -> & など)
                            string imageUrl = HttpUtility.HtmlDecode(rawImageUrl);
                            LogHelper.LogAnalysis($"[HTML] デコード後画像URL: {imageUrl}");

                            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri uri))
                            {
                                string tempPath = await DownloadImageFromUrl(uri);
                                if (!string.IsNullOrEmpty(tempPath))
                                {
                                    fileList.Add(tempPath);
                                }
                            }
                        }
                        else
                        {
                            LogHelper.LogAnalysis("[HTML] img タグが見つかりませんでした。UnicodeText を試します。");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogError("[HTMLダウンロード] 失敗: " + ex.Message);
                }
            }
            // URL（UnicodeText） - HTMLから取得できなかった場合のフォールバック
            else if (dataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                try
                {
                    string url = dataObject.GetData(DataFormats.UnicodeText) as string;
                    if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                    {
                        LogHelper.LogAnalysis($"[URL] 画像URL: {url}");
                        string tempPath = await DownloadImageFromUrl(uri);
                        if (!string.IsNullOrEmpty(tempPath))
                        {
                            fileList.Add(tempPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogError("[URLダウンロード] 失敗: " + ex.Message);
                }
            }

            return fileList;
        }

        private bool TryGetValidVirtualFile(IDataObject dataObject, out Stream stream, out string fileName)
        {
            stream = null;
            fileName = null;

            try
            {
                // base64エンコードされた画像データの処理
                string[] formatsToCheck = { DataFormats.Html, DataFormats.Text, DataFormats.UnicodeText, "System.String" };

                foreach (var format in formatsToCheck)
                {
                    if (dataObject.GetDataPresent(format))
                    {
                        string text = dataObject.GetData(format) as string;
                        if (!string.IsNullOrEmpty(text))
                        {
                            // data:image/jpeg;base64,... のパターンを検出
                            var match = Regex.Match(text, @"data:(?<mime>image/[^;]+);base64,(?<base64>[^""'\s]+)");
                            if (match.Success)
                            {
                                string mimeType = match.Groups["mime"].Value;
                                string base64Data = match.Groups["base64"].Value;

                                // MIMEタイプから拡張子を決定
                                string ext = mimeType switch
                                {
                                    "image/jpeg" => ".jpg",
                                    "image/png" => ".png",
                                    "image/gif" => ".gif",
                                    "image/webp" => ".webp",
                                    _ => ".jpg" // デフォルト
                                };

                                try
                                {
                                    // base64をデコード
                                    byte[] imageData = Convert.FromBase64String(base64Data);
                                    stream = new MemoryStream(imageData);
                                    fileName = $"clipboard_image_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
                                    return true;
                                }
                                catch (Exception ex)
                                {
                                    LogHelper.LogError($"[Base64] デコード失敗: {ex.Message}");
                                }
                            }
                        }
                    }
                }

                // 通常の仮想ファイル処理
                if (!dataObject.GetDataPresent("FileGroupDescriptorW")) return false;

                // ファイル名
                var descriptor = dataObject.GetData("FileGroupDescriptorW") as MemoryStream;
                var buf = new byte[descriptor.Length];
                descriptor.Read(buf, 0, buf.Length);
                fileName = Encoding.Unicode.GetString(buf, 76, 520).TrimEnd('\0');

                // 中身
                var raw = dataObject.GetData("FileContents", true);
                if (raw is MemoryStream ms)
                    stream = ms;
                else if (raw is Stream s)
                    stream = s;
                else if (raw is Array arr && arr.Length > 0 && arr.GetValue(0) is Stream s0)
                    stream = s0;
                else
                    return false;

                return stream != null;
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"[仮想ファイル] 処理エラー: {ex.Message}");
                return false;
            }
        }

        private async Task<string> DownloadImageFromUrl(Uri uri)
        {
            try
            {
                // URLからファイル名を取得
                string baseFileName = Path.GetFileNameWithoutExtension(uri.LocalPath);
                if (string.IsNullOrWhiteSpace(baseFileName) || !FileHelper.IsValidFileName(baseFileName + ".tmp"))
                {
                    baseFileName = "downloaded_image";
                }

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    var bytes = await client.GetByteArrayAsync(uri);

                    // 拡張子の判定
                    string? actualExtension = FileHelper.GetImageExtensionFromBytes(bytes);
                    if (string.IsNullOrEmpty(actualExtension))
                    {
                        actualExtension = Path.GetExtension(uri.LocalPath)?.ToLowerInvariant();
                        if (string.IsNullOrEmpty(actualExtension) || !FileHelper.SupportedExtensions.Contains(actualExtension))
                        {
                            LogHelper.LogAnalysis($"[URL] 拡張子不明または非対応 ({actualExtension})。デフォルトで.jpgを使用します。");
                            actualExtension = ".jpg";
                        }
                        else
                        {
                            LogHelper.LogAnalysis($"[URL] バイト判定失敗。URLの拡張子 ({actualExtension}) を使用します。");
                        }
                    }

                    string finalFileName = baseFileName + actualExtension;
                    string tempPath = Path.Combine(Path.GetTempPath(), finalFileName);
                    await File.WriteAllBytesAsync(tempPath, bytes);
                    return tempPath;
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"[URLダウンロード] 失敗: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
