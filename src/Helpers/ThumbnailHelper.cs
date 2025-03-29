using Microsoft.WindowsAPICodePack.Shell;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using SkiaSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Globalization;

namespace Illustra.Helpers
{
    public static class VideoThumbnailHelper
    {
        /// <summary>
        /// 動画ファイルからサムネイルを作成します
        /// </summary>
        /// <param name="videoPath">動画ファイルのパス</param>
        /// <returns>サムネイル画像。失敗した場合はnull</returns>
        public static BitmapSource? GetVideoThumbnail(string videoPath)
        {
            try
            {
                using (var shellFile = ShellFile.FromFilePath(videoPath))
                {
                    var bitmap = shellFile.Thumbnail.ExtraLargeBitmap;
                    if (bitmap == null) return null;

                    using var memory = new MemoryStream();
                    bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                    memory.Position = 0;

                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = memory;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    return bitmapImage;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"動画サムネイル生成エラー ({videoPath}): {ex.Message}");
                return null;
            }
        }
    }

    public static class ThumbnailHelper
    {
        /// <summary>
        /// 画像ファイルからサムネイルを作成します。縮小デコードを使用して効率的に処理します。
        /// </summary>
        /// <param name="imagePath">画像ファイルのパス</param>
        /// <param name="width">サムネイルの幅</param>
        /// <param name="height">サムネイルの高さ</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>BitmapSourceとしてのサムネイル画像</returns>
        public static BitmapSource CreateThumbnail(string filePath, int width, int height, CancellationToken cancellationToken = default)
        {
            try
            {
                // キャンセルされたかチェック
                cancellationToken.ThrowIfCancellationRequested();

                // mp4ファイルの場合は専用の処理を使用
                if (Path.GetExtension(filePath).ToLower() == ".mp4")
                {
                    var videoThumbnail = VideoThumbnailHelper.GetVideoThumbnail(filePath);
                    if (videoThumbnail != null)
                    {
                        return videoThumbnail;
                    }
                    return GenerateVideoErrorThumbnail(width, height);
                }

                // 以下、既存の画像サムネイル生成処理
                // ファイルからビットマップデータを読み込む
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                using var inputStream = new SKManagedStream(fileStream);

                // 既存の実装を維持（互換性のため）
                using var originalBitmap = SKBitmap.Decode(inputStream);

                // キャンセルされたかチェック
                cancellationToken.ThrowIfCancellationRequested();

                if (originalBitmap == null)
                {
                    return GenerateErrorThumbnail(width, height, "画像の読み込みに失敗しました");
                }

                // 縦横比を維持したリサイズ計算
                float aspectRatio = (float)originalBitmap.Width / originalBitmap.Height;
                int newWidth, newHeight;

                if (aspectRatio > 1)
                {
                    // 横長画像
                    newWidth = width;
                    newHeight = (int)(width / aspectRatio);
                }
                else
                {
                    // 縦長画像
                    newHeight = height;
                    newWidth = (int)(height * aspectRatio);
                }

                // キャンセルされたかチェック
                cancellationToken.ThrowIfCancellationRequested();

                // リサイズ
                using var resizedBitmap = originalBitmap.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.Medium);

                // WriteableBitmapを使用して直接メモリにコピー
                var writeableBitmap = new WriteableBitmap(newWidth, newHeight, 96, 96, PixelFormats.Bgra32, null);
                writeableBitmap.Lock();
                CopySkBitmapToWriteableBitmap(resizedBitmap, writeableBitmap);
                writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, newWidth, newHeight));
                writeableBitmap.Unlock();
                writeableBitmap.Freeze(); // UIスレッド渡すなら必須

                return writeableBitmap;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[CANCELLED] サムネイルの作成処理がキャンセルされました: {filePath}");
                throw; // キャンセル例外は上位に伝播
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サムネイル作成エラー ({filePath}): {ex.Message}");
                // 動画ファイルのエラーの場合は専用サムネイルを返す
                if (Path.GetExtension(filePath).ToLower() == ".mp4")
                {
                    return GenerateVideoErrorThumbnail(width, height);
                }
                return GenerateErrorThumbnail(width, height, "サムネイル作成エラー");
            }
        }

        /// <summary>
        /// 画像ファイルからサムネイルを作成します。縮小デコードを使用して効率的に処理します。
        /// </summary>
        /// <param name="imagePath">画像ファイルのパス</param>
        /// <param name="width">サムネイルの幅</param>
        /// <param name="height">サムネイルの高さ</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>BitmapSourceとしてのサムネイル画像</returns>
        public static BitmapSource CreateThumbnailOptimized(string imagePath, int width, int height, CancellationToken cancellationToken = default)
        {
            try
            {
                // キャンセルチェック
                cancellationToken.ThrowIfCancellationRequested();

                // ファイルストリーム準備
                using var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var inputStream = new SKManagedStream(fileStream);

                // コーデック準備
                using var codec = SKCodec.Create(inputStream);
                if (codec == null)
                    return GenerateErrorThumbnail(width, height, "画像の読み込みに失敗しました");

                cancellationToken.ThrowIfCancellationRequested();

                // 元画像サイズとアスペクト比
                var originalInfo = codec.Info;
                float aspectRatio = (float)originalInfo.Width / originalInfo.Height;

                // サムネイルサイズ計算
                int targetWidth, targetHeight;
                if (aspectRatio > 1)
                {
                    targetWidth = width;
                    targetHeight = Math.Max(1, (int)(width / aspectRatio));
                }
                else
                {
                    targetHeight = height;
                    targetWidth = Math.Max(1, (int)(height * aspectRatio));
                }

                // スケールファクターとサンプルサイズ
                float scaleFactor = Math.Min((float)targetWidth / originalInfo.Width, (float)targetHeight / originalInfo.Height);
                int sampleSize = scaleFactor <= 0.125f ? 8 : scaleFactor <= 0.25f ? 4 : scaleFactor <= 0.5f ? 2 : 1;

                // オプション（ゼロ除算防止）
                var options = new SKImageInfo(
                    width: Math.Max(1, originalInfo.Width / sampleSize),
                    height: Math.Max(1, originalInfo.Height / sampleSize),
                    colorType: SKColorType.Bgra8888,
                    alphaType: SKAlphaType.Premul);

                // デコード
                using var bitmap = SKBitmap.Decode(codec, options);
                if (bitmap == null)
                    return CreateThumbnail(imagePath, width, height, cancellationToken); // 失敗時は通常の方法で;

                cancellationToken.ThrowIfCancellationRequested();

                // リサイズ処理
                SKBitmap finalBitmap = null;
                bool needResize = bitmap.Width != targetWidth || bitmap.Height != targetHeight;
                if (needResize)
                {
                    finalBitmap = bitmap.Resize(new SKImageInfo(targetWidth, targetHeight), SKFilterQuality.Medium);
                    if (finalBitmap == null)
                        return CreateThumbnail(imagePath, width, height, cancellationToken); // 失敗時は通常の方法で;
                }
                else
                {
                    finalBitmap = bitmap; // そのまま利用
                }

                // WriteableBitmap作成
                var writeableBitmap = new WriteableBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Bgra32, null);
                writeableBitmap.Lock();
                try
                {
                    CopySkBitmapToWriteableBitmap(finalBitmap, writeableBitmap);
                    writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, targetWidth, targetHeight));
                }
                finally
                {
                    writeableBitmap.Unlock();
                }
                writeableBitmap.Freeze();

                // bitmapとfinalBitmapが同じ場合、二重Dispose防止
                if (!ReferenceEquals(bitmap, finalBitmap))
                {
                    finalBitmap.Dispose();
                }

                return writeableBitmap;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[CANCELLED] サムネイルの作成処理がキャンセルされました: {imagePath}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サムネイル作成エラー ({imagePath}): {ex.Message}");
                return GenerateErrorThumbnail(width, height, "サムネイル作成エラー");
            }
        }


        /// <summary>
        /// 高品質な画像表示用に画像をロードします。フルスクリーン表示などに適しています。
        /// </summary>
        /// <param name="imagePath">画像ファイルのパス</param>
        /// <param name="maxWidth">最大幅（0の場合は制限なし）</param>
        /// <param name="maxHeight">最大高さ（0の場合は制限なし）</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>BitmapSourceとしての画像</returns>
        public static BitmapSource LoadImage(string imagePath, int maxWidth = 0, int maxHeight = 0, CancellationToken cancellationToken = default)
        {
            try
            {
                // キャンセルされたかチェック
                cancellationToken.ThrowIfCancellationRequested();

                // ファイルからビットマップデータを読み込む
                using var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);

                // 画像のメタデータを取得して、必要に応じて縮小デコードを行う
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = fileStream;

                // 最大サイズが指定されている場合は、それに合わせて縮小
                if (maxWidth > 0) bitmapImage.DecodePixelWidth = maxWidth;
                if (maxHeight > 0) bitmapImage.DecodePixelHeight = maxHeight;

                bitmapImage.EndInit();
                bitmapImage.Freeze(); // UIスレッドでの使用のために凍結

                return bitmapImage;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[CANCELLED] 画像の読み込み処理がキャンセルされました: {imagePath}");
                throw; // キャンセル例外は上位に伝播
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像読み込みエラー ({imagePath}): {ex.Message}");
                int errorSize = 200;
                return GenerateErrorThumbnail(errorSize, errorSize, "画像読み込みエラー");
            }
        }

        /// <summary>
        /// 大きな画像を効率的にロードします。SKCodecを使用して縮小デコードを行います。
        /// </summary>
        /// <param name="imagePath">画像ファイルのパス</param>
        /// <param name="maxWidth">最大幅</param>
        /// <param name="maxHeight">最大高さ</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>BitmapSourceとしての画像</returns>
        public static BitmapSource LoadLargeImage(string imagePath, int maxWidth, int maxHeight, CancellationToken cancellationToken = default)
        {
            try
            {
                // キャンセルされたかチェック
                cancellationToken.ThrowIfCancellationRequested();

                // ファイルからビットマップデータを読み込む
                using var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                using var inputStream = new SKManagedStream(fileStream);

                // SKCodecを使用して画像情報を取得
                using var codec = SKCodec.Create(inputStream);
                if (codec == null)
                {
                    return GenerateErrorThumbnail(400, 300, "画像の読み込みに失敗しました");
                }

                // 元画像のサイズを取得
                var originalInfo = codec.Info;

                // 縮小率を計算
                float scaleX = maxWidth > 0 ? (float)maxWidth / originalInfo.Width : 1.0f;
                float scaleY = maxHeight > 0 ? (float)maxHeight / originalInfo.Height : 1.0f;
                float scale = Math.Min(scaleX, scaleY);

                // 縮小が必要ない場合は等倍で読み込む
                if (scale >= 1.0f || (maxWidth == 0 && maxHeight == 0))
                {
                    using var fullBitmap = SKBitmap.Decode(codec);
                    if (fullBitmap == null)
                    {
                        return GenerateErrorThumbnail(400, 300, "画像のデコードに失敗しました");
                    }

                    // WriteableBitmapを使用して直接メモリにコピー
                    var writeableBitmapFull = new WriteableBitmap(fullBitmap.Width, fullBitmap.Height, 96, 96, PixelFormats.Bgra32, null);
                    writeableBitmapFull.Lock();
                    CopySkBitmapToWriteableBitmap(fullBitmap, writeableBitmapFull);
                    writeableBitmapFull.AddDirtyRect(new Int32Rect(0, 0, fullBitmap.Width, fullBitmap.Height));
                    writeableBitmapFull.Unlock();
                    writeableBitmapFull.Freeze();

                    return writeableBitmapFull;
                }

                // 縮小が必要な場合は、サンプルサイズを選択
                var sampleSize = 1;
                if (scale <= 0.125f) sampleSize = 8;
                else if (scale <= 0.25f) sampleSize = 4;
                else if (scale <= 0.5f) sampleSize = 2;

                // 縮小後のサイズを計算
                int targetWidth = (int)(originalInfo.Width * scale);
                int targetHeight = (int)(originalInfo.Height * scale);

                // デコードオプションを設定
                var options = new SKImageInfo(
                    width: originalInfo.Width / sampleSize,
                    height: originalInfo.Height / sampleSize,
                    colorType: SKColorType.Bgra8888,
                    alphaType: SKAlphaType.Premul);

                // 縮小デコード
                using var scaledBitmap = SKBitmap.Decode(codec, options);
                if (scaledBitmap == null)
                {
                    return GenerateErrorThumbnail(400, 300, "画像のデコードに失敗しました");
                }

                // キャンセルされたかチェック
                cancellationToken.ThrowIfCancellationRequested();

                // 必要に応じて正確なサイズにリサイズ
                using var resizedBitmap = scaledBitmap.Width == targetWidth && scaledBitmap.Height == targetHeight
                    ? scaledBitmap
                    : scaledBitmap.Resize(new SKImageInfo(targetWidth, targetHeight), SKFilterQuality.High);

                // WriteableBitmapを使用して直接メモリにコピー
                var writeableBitmapScaled = new WriteableBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Bgra32, null);
                writeableBitmapScaled.Lock();
                CopySkBitmapToWriteableBitmap(resizedBitmap, writeableBitmapScaled);
                writeableBitmapScaled.AddDirtyRect(new Int32Rect(0, 0, targetWidth, targetHeight));
                writeableBitmapScaled.Unlock();
                writeableBitmapScaled.Freeze();

                return writeableBitmapScaled;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[CANCELLED] 大きな画像の読み込み処理がキャンセルされました: {imagePath}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"大きな画像の読み込みエラー ({imagePath}): {ex.Message}");
                return GenerateErrorThumbnail(400, 300, "画像読み込みエラー");
            }
        }

        /// <summary>
        /// SKBitmapからWriteableBitmapへのピクセルデータのコピーを行います
        /// </summary>
        private static void CopySkBitmapToWriteableBitmap(SKBitmap skBitmap, WriteableBitmap writeableBitmap)
        {
            // SKBitmapのピクセルデータをコピー
            IntPtr sourcePtr = skBitmap.GetPixels();
            int byteCount = skBitmap.ByteCount;

            // バイト配列を介してコピー
            byte[] tempBuffer = new byte[byteCount];
            Marshal.Copy(sourcePtr, tempBuffer, 0, byteCount);
            Marshal.Copy(tempBuffer, 0, writeableBitmap.BackBuffer, byteCount);
        }

        /// <summary>
        /// エラー時に表示するサムネイルを生成します
        /// </summary>
        public static BitmapSource GenerateErrorThumbnail(int width, int height, string errorMessage = "Error")
        {
            // エラーメッセージを含む簡易サムネイルを作成
            var info = new SKImageInfo(width, height);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;

            // 背景を薄い赤色で塗る
            canvas.Clear(new SKColor(255, 200, 200));

            // 枠線を描画
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = new SKColor(220, 50, 50),
                StrokeWidth = 2
            };
            canvas.DrawRect(2, 2, width - 4, height - 4, borderPaint);

            // エラー記号（X）を描画
            using var xPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.Red,
                StrokeWidth = 3,
                IsAntialias = true
            };
            canvas.DrawLine(width * 0.3f, height * 0.3f, width * 0.7f, height * 0.7f, xPaint);
            canvas.DrawLine(width * 0.7f, height * 0.3f, width * 0.3f, height * 0.7f, xPaint);

            // 短いエラーメッセージを表示
            if (!string.IsNullOrEmpty(errorMessage) && errorMessage.Length > 20)
            {
                errorMessage = errorMessage.Substring(0, 20) + "...";
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                using var textPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    TextSize = 10,
                    IsAntialias = true
                };
                canvas.DrawText("Error", width / 2 - 15, height * 0.85f, textPaint);
            }

            // SKImageからWriteableBitmapに直接変換
            using var skImage = surface.Snapshot();
            using var skBitmap = SKBitmap.FromImage(skImage);

            var errorBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            errorBitmap.Lock();
            CopySkBitmapToWriteableBitmap(skBitmap, errorBitmap);
            errorBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            errorBitmap.Unlock();
            errorBitmap.Freeze();

            return errorBitmap;
        }

        /// <summary>
        /// 動画ファイル用のエラーサムネイルを生成します
        /// </summary>
        public static BitmapSource GenerateVideoErrorThumbnail(int width, int height)
        {
            var info = new SKImageInfo(width, height);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;

            // 背景を灰色で塗る
            canvas.Clear(new SKColor(240, 240, 240));

            // 枠線を描画
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = new SKColor(180, 180, 180),
                StrokeWidth = 2
            };
            canvas.DrawRect(2, 2, width - 4, height - 4, borderPaint);

            // 再生アイコンを描画
            using var playPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = new SKColor(180, 180, 180),
                IsAntialias = true
            };

            // 中央に三角形を描画
            float centerX = width / 2;
            float centerY = height / 2;
            float size = Math.Min(width, height) * 0.3f;

            var path = new SKPath();
            path.MoveTo(centerX - size/2, centerY - size/2);
            path.LineTo(centerX + size/2, centerY);
            path.LineTo(centerX - size/2, centerY + size/2);
            path.Close();
            canvas.DrawPath(path, playPaint);

            // "Video" テキストを表示
            using var textPaint = new SKPaint
            {
                Color = SKColors.Gray,
                TextSize = 12,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center
            };
            canvas.DrawText("Video", width / 2, height * 0.85f, textPaint);

            // SKImageからWriteableBitmapに変換
            using var skImage = surface.Snapshot();
            using var skBitmap = SKBitmap.FromImage(skImage);

            var videoBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            videoBitmap.Lock();
            CopySkBitmapToWriteableBitmap(skBitmap, videoBitmap);
            videoBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            videoBitmap.Unlock();
            videoBitmap.Freeze();

            return videoBitmap;
        }

    }
}
