using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageMagick;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace Illustra.Helpers
{
    /// <summary>
    /// WebP画像に関するヘルパーメソッドを提供します。
    /// </summary>
    public static class WebPHelper
    {
        private static readonly string WebView2Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView2");

        /// <summary>
        /// WebView2 がインストールされているかを確認する
        /// </summary>
        /// <returns>インストールされていれば true</returns>
        public static bool IsWebView2Installed()
        {
            // 1️⃣ GetAvailableCoreWebView2BrowserVersionString() でチェック（推奨）
            string version = GetWebView2Version();
            if (!string.IsNullOrEmpty(version))
            {
                Console.WriteLine($"WebView2 detected via API: {version}");
                return true;
            }

            // 2️⃣ レジストリをチェック
            string[] registryKeys =
            {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F38A9D3D-F40D-40A2-BF8A-535002C6AE93}",
                @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F38A9D3D-F40D-40A2-BF8A-535002C6AE93}",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F38A9D3D-F40D-40A2-BF8A-535002C6AE93}"
            };

            foreach (var key in registryKeys)
            {
                string regVersion = Registry.GetValue(key, "pv", null) as string;
                if (!string.IsNullOrEmpty(regVersion))
                {
                    Console.WriteLine($"WebView2 detected via registry: {regVersion}");
                    return true;
                }
            }

            // 3️⃣ `msedgewebview2.exe` の実行ファイルを探す
            string basePath = @"C:\Program Files (x86)\Microsoft\EdgeWebView\Application";
            if (GetLatestWebView2Path(basePath) != null)
            {
                Console.WriteLine("WebView2 detected via file system.");
                return true;
            }

            return false;
        }

        /// <summary>
        /// GetAvailableCoreWebView2BrowserVersionString() を使って WebView2 のバージョンを取得
        /// </summary>
        private static string GetWebView2Version()
        {
            try
            {
                string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                return version;
            }
            catch (COMException)
            {
                return null;
            }
        }

        /// <summary>
        /// WebView2 の `msedgewebview2.exe` の最新パスを取得
        /// </summary>
        private static string GetLatestWebView2Path(string basePath)
        {
            if (!Directory.Exists(basePath))
            {
                return null;
            }

            var versionFolders = Directory.GetDirectories(basePath)
                .Select(Path.GetFileName)
                .Where(f => Version.TryParse(f, out _)) // バージョン番号のフォルダを取得
                .OrderByDescending(f => f) // 最新バージョンを取得
                .ToList();

            if (versionFolders.Count > 0)
            {
                string latestVersionPath = Path.Combine(basePath, versionFolders[0], "msedgewebview2.exe");
                if (File.Exists(latestVersionPath))
                {
                    return latestVersionPath;
                }
            }

            return null;
        }


        private static string GetLatestWebView2Path()
        {
            string basePath = @"C:\Program Files (x86)\Microsoft\EdgeWebView\Application";
            if (!Directory.Exists(basePath)) return null;

            var versionFolders = Directory.GetDirectories(basePath)
                .Select(Path.GetFileName)
                .Where(f => Version.TryParse(f, out _))
                .OrderByDescending(f => f)
                .ToList();

            if (versionFolders.Count > 0)
            {
                return Path.Combine(basePath, versionFolders[0]);
            }

            return null;
        }
        /// <summary>
        /// アニメーション付きWebPを表示します。
        /// </summary>
        /// <param name="webView">WebViewコントロール</param>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="fitToScreen">true: 画面にフィットさせる、false: 可能な場合は1:1で表示</param>
        public static async Task ShowAnimatedWebPAsync(WebView2 webView, string filePath, bool fitToScreen = false)
        {
            try
            {
                if (!IsWebView2Installed())
                {
                    MessageBox.Show("WebP アニメーションの表示には WebView2 が必要です。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (webView.CoreWebView2 == null)
                {
                    // WebView2 のバージョンを取得（これでインストール確認）
                    string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                    if (string.IsNullOrEmpty(version))
                    {
                        throw new Exception("WebView2 Runtime がインストールされていません！");
                    }
                    // WebView2 のインスタンスを初期化
                    await EnsureCoreWebView2Async(webView);
                }

                string htmlContent = GenerateAnimationHtml(filePath, fitToScreen);
                var hash = filePath.GetHashCode() + DateTime.Now.Ticks.ToString("x");
                string tempHtmlPath = Path.Combine(Path.GetTempPath(), $"webp_viewer_{hash}.html");
                File.WriteAllText(tempHtmlPath, htmlContent);
                webView.Source = new Uri(tempHtmlPath);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("[WebPHelper] アニメーションWebPの表示に失敗", ex);
            }
        }

        private static void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // JSON文字列をデシリアライズ
                var jsonDocument = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
                var root = jsonDocument.RootElement;

                if (root.TryGetProperty("type", out var typeProperty) && typeProperty.GetString() == "wheel")
                {
                    // ホイールイベントからデルタ値を取得
                    double deltaY = root.GetProperty("deltaY").GetDouble();

                    // ここでホイールイベントを処理
                    // 例: 画像のズームイン/ズームアウト
                    if (deltaY < 0)
                    {
                        // ズームイン処理
                        Console.WriteLine("Zoom in");
                    }
                    else
                    {
                        // ズームアウト処理
                        Console.WriteLine("Zoom out");
                    }

                    // 必要に応じて独自のイベントを発行したり、メソッドを呼び出したりする
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing WebView2 message: {ex.Message}");
            }
        }

        public static async Task<bool> EnsureCoreWebView2Async(WebView2 webView)
        {
            if (webView.CoreWebView2 == null)
            {
                // ネットワーク接続禁止
                var options = new CoreWebView2EnvironmentOptions("--no-sandbox --no-proxy-server --disable-features=NetworkService,OutOfBlinkCors");
                string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebView2UserData");
                var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                await webView.EnsureCoreWebView2Async(environment);

                // ネットワーク接続禁止
                webView.CoreWebView2.WebResourceRequested += (s, e) =>
                    {
                        var uri = e.Request.Uri;
                        if (!uri.StartsWith("file://"))
                        {
                            e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Forbidden", null);
                        }
                    };
                return true;
            }
            return false;
        }

        /// <summary>
        /// アニメーション表示用のHTMLを生成します。
        /// </summary>
        private static string GenerateAnimationHtml(string filePath, bool fitToScreen = true)
        {
            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='utf-8'>
                    <style>
                        html, body {{
                            margin: 0;
                            padding: 0;
                            width: 100vw;
                            height: 100vh;
                            overflow: hidden;
                        }}
                        #image-container {{
                            position: absolute;
                            top: 0;
                            left: 0;
                            width: 100%;
                            height: 100%;
                            overflow: hidden;
                        }}
                        img {{
                            position: absolute;
                            transform-origin: 0 0;
                            pointer-events: none;
                            user-select: none; /* 選択を無効化 */
                            -webkit-user-select: none; /* Chromium対応 */
                            -moz-user-select: none; /* Firefox対応 */
                            -ms-user-select: none; /* IE対応 */
                        }}
                    </style>
                    <script>
                        window.fitToScreen = {(fitToScreen ? "true" : "false")};
                        window.imageState = {{
                            scale: 1.0,
                            offsetX: 0,
                            offsetY: 0,
                            lastWheelTime: 0
                        }};

                        let isDragging = false;
                        let startX, startY;

                        function updateImageTransform() {{
                            const img = document.querySelector('img');
                            if (!img) return;

                            img.style.transform = `translate(${{window.imageState.offsetX}}px, ${{window.imageState.offsetY}}px) scale(${{window.imageState.scale}})`;
                            console.log(`Updated transform: translate(${{window.imageState.offsetX}}px, ${{window.imageState.offsetY}}px) scale(${{window.imageState.scale}})`);
                            console.log(`Scaled size: ${{img.naturalWidth * window.imageState.scale}}x${{img.naturalHeight * window.imageState.scale}}`);
                        }}

                        function centerImage() {{
                            console.log('centerImage() called');

                            const img = document.querySelector('img');
                            if (!img) {{
                                console.warn('centerImage: Image not found');
                                return;
                            }}

                            const containerWidth = window.innerWidth;
                            const containerHeight = window.innerHeight;
                            const imgWidth = img.naturalWidth || img.width;
                            const imgHeight = img.naturalHeight || img.height;

                            console.log(`Image Size: ${{imgWidth}}x${{imgHeight}}, Container Size: ${{containerWidth}}x${{containerHeight}}`);

                            // fitToScreenがfalseの場合、可能であれば1:1で表示
                            if (!window.fitToScreen && imgWidth <= containerWidth && imgHeight <= containerHeight) {{
                                window.imageState.scale = 1.0;
                            }} else {{
                                window.imageState.scale = Math.min(containerWidth / imgWidth, containerHeight / imgHeight);
                            }}

                            const scaledWidth = imgWidth * window.imageState.scale;
                            const scaledHeight = imgHeight * window.imageState.scale;

                            window.imageState.offsetX = (containerWidth - scaledWidth) / 2;
                            window.imageState.offsetY = (containerHeight - scaledHeight) / 2;

                            console.log('Scale: ' + window.imageState.scale + ', Fit to screen: ' + window.fitToScreen);

                            updateImageTransform();
                        }}

                        function mouseDown(e) {{
                            isDragging = true;
                            startX = e.clientX - window.imageState.offsetX;
                            startY = e.clientY - window.imageState.offsetY;
                            document.body.style.cursor = 'pointer';
                        }}

                        function mouseMove(e) {{
                            if (!isDragging) return;
                            window.imageState.offsetX = e.clientX - startX;
                            window.imageState.offsetY = e.clientY - startY;
                            updateImageTransform();
                        }}

                        function mouseUp() {{
                            isDragging = false;
                            document.body.style.cursor = 'default';
                        }}

                        function zoomImage(direction, mouseX, mouseY) {{
                            const zoomFactor = 1.2;
                            const oldScale = window.imageState.scale;
                            window.imageState.scale = direction > 0
                                ? window.imageState.scale * zoomFactor
                                : window.imageState.scale / zoomFactor;

                            window.imageState.scale = Math.min(Math.max(0.5, window.imageState.scale), 10);

                            window.imageState.offsetX -= (mouseX - window.imageState.offsetX) * (window.imageState.scale / oldScale - 1);
                            window.imageState.offsetY -= (mouseY - window.imageState.offsetY) * (window.imageState.scale / oldScale - 1);

                            updateImageTransform();
                        }}

                        function handleWheel(e) {{
                            if (e.ctrlKey) {{
                                const now = Date.now();
                                if (now - window.imageState.lastWheelTime < 50) {{
                                    e.preventDefault();
                                    return;
                                }}
                                window.imageState.lastWheelTime = now;

                                const direction = e.deltaY < 0 ? 1 : -1;
                                zoomImage(direction, e.clientX, e.clientY);

                                e.preventDefault();
                                e.stopPropagation();
                            }} else {{
                                try {{
                                    window.chrome.webview.postMessage({{
                                        type: 'wheel',
                                        deltaY: e.deltaY / 100 * 120 * -1,
                                    }});
                                }} catch (err) {{
                                    console.error('Failed to send message:', err);
                                }}
                                e.preventDefault();
                            }}
                        }}

                        function doubleClick(e) {{
                            try {{
                                window.chrome.webview.postMessage({{
                                    type: 'dblclick',
                                    clientX: e.clientX,
                                    clientY: e.clientY
                                }});
                            }} catch (err) {{
                                console.error('Failed to send dblclick message:', err);
                            }}
                        }}

                        window.onload = function() {{
                            const img = document.querySelector('img');
                            if (img) {{
                                console.log('window.onload: Image found');

                                img.onload = centerImage;

                                if (img.complete) {{
                                    console.log('Image already loaded, calling centerImage()');
                                    centerImage();
                                }}

                                updateImageTransform();
                                img.ondragstart = () => false;
                                img.onmousedown = (e) => e.preventDefault();
                            }} else {{
                                console.warn('window.onload: No image found');
                            }}

                            document.addEventListener('wheel', handleWheel, {{ passive: false, capture: true }});
                            document.addEventListener('mousemove', mouseMove, {{ passive: false, capture: true }});
                            document.addEventListener('mouseup', mouseUp, {{ passive: false, capture: true }});
                            document.addEventListener('mousedown', mouseDown, {{ passive: false, capture: true }});
                            document.addEventListener('dblclick', doubleClick, {{ passive: false, capture: true }});
                            document.addEventListener('keydown', (e) => {{
                                console.log(`Key down: ${{e.key}}, code: ${{e.code}}`);
                                try {{
                                    window.chrome.webview.postMessage({{
                                        type: 'keydown',
                                        key: e.key,
                                        code: e.code
                                    }});
                                    e.preventDefault();
                                }} catch (err) {{
                                    console.error('Failed to send keydown message:', err);
                                }}
                            }});

                            // サイズ変更イベントを監視
                            window.onresize = function() {{
                                console.log('Window resized');
                                centerImage();
                            }};
                            console.log('Page initialized with scale:', window.imageState.scale);
                        }};
                    </script>
                </head>
                <body>
                    <div id='image-container'>
                        <img src='file:///{filePath.Replace("\\", "/")}' />
                    </div>
                </body>
            </html>";
        }

        public static string ConvertBitmapSourceToDataUri(BitmapSource bitmap)
        {
            if (bitmap == null) return "about:blank"; // 🔥 `null` なら `about:blank` を返す！

            using (MemoryStream stream = new MemoryStream())
            {
                BitmapEncoder encoder = new PngBitmapEncoder(); // PNGエンコーダーを使う
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);

                string base64 = Convert.ToBase64String(stream.ToArray());
                return $"data:image/png;base64,{base64}";
            }
        }

        public static Uri GetTransparentDataUri()
        {
            return new Uri("data:image/png;base64," +
                   "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/wcAAwAB/gn5mwAAAABJRU5ErkJggg==");
        }

        public static BitmapImage GetTransparentBitmapImage()
        {
            string base64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/wcAAwAB/gn5mwAAAABJRU5ErkJggg==";
            byte[] bytes = Convert.FromBase64String(base64);

            using (MemoryStream stream = new MemoryStream(bytes))
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze(); // 🔥 WPF で UI スレッド以外でも使えるようにする！
                return image;
            }
        }

        /// <summary>
        /// 指定されたWebP画像ファイルがアニメーションを含むかどうかを判定します。
        /// </summary>
        public static bool IsAnimatedWebP(string filePath)
        {
            try
            {
                using var images = new MagickImageCollection(filePath);
                return images.Count > 1;
            }
            catch
            {
                return false;
            }
        }


        // 公式リンクからインストールしてもらうので、インストーラーは埋め込まない
        public static async Task CheckAndInstallWebView2()
        {
            if (IsWebView2Installed())
            {
                MessageBox.Show("WebView2 Runtime は既にインストールされています。");
                return;
            }
            else
            {
                MessageBox.Show("WebView2 Runtime が見つかりません。インストールを開始します。");

                await InstallWebView2Async();
            }
        }

        /// <summary>
        /// WebView2 インストーラーをリソースから展開して実行
        /// </summary>
        private static async Task<bool> InstallWebView2Async()
        {
            try
            {
                string installerPath = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebView2Setup.exe");

                // 埋め込みリソースを取得
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream resourceStream = assembly.GetManifestResourceStream("Illustra.Resources.MicrosoftEdgeWebView2Setup.exe"))
                using (FileStream fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write))
                {
                    if (resourceStream == null)
                    {
                        MessageBox.Show("リソースの取得に失敗しました");
                        return false;
                    }
                    await resourceStream.CopyToAsync(fileStream);
                }

                // インストーラーをサイレントモードで実行
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/install",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process process = Process.Start(psi);
                if (process == null) return false;

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 インストールエラー: {ex.Message}");
                return false;
            }
        }

        internal static async Task InitializeWebView2Async(WebView2 webView)
        {
            if (webView.CoreWebView2 == null)
            {
                var environment = await CoreWebView2Environment.CreateAsync(WebView2Path);
                await webView.EnsureCoreWebView2Async(environment);
            }
        }
    }
}
