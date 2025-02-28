using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace Illustra.Helpers
{
    public static class ThumbnailHelper
    {
        /// <summary>
        /// 画像ファイルからサムネイルを作成します。高品質なスケーリングアルゴリズムを使用します。
        /// </summary>
        /// <param name="imagePath">画像ファイルのパス</param>
        /// <param name="width">サムネイルの幅</param>
        /// <param name="height">サムネイルの高さ</param>
        /// <returns>BitmapSourceとしてのサムネイル画像</returns>
        public static async Task<BitmapSource> CreateThumbnailAsync(string imagePath, int width, int height)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // ファイルからビットマップデータを読み込む
                    using var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                    using var inputStream = new SKManagedStream(fileStream);
                    using var originalBitmap = SKBitmap.Decode(inputStream);

                    if (originalBitmap == null)
                    {
                        return GenerateErrorThumbnail(width, height, "画像の読み込みに失敗しました");
                    }

                    // 縦横比を維持したリサイズ計算
                    float aspectRatio = (float)originalBitmap.Width / originalBitmap.Height;
                    int newWidth, newHeight;

                    if (aspectRatio > 1) // 横長画像
                    {
                        newWidth = width;
                        newHeight = (int)(width / aspectRatio);
                    }
                    else // 縦長または正方形
                    {
                        newHeight = height;
                        newWidth = (int)(height * aspectRatio);
                    }

                    // 新しいサイズが指定サイズを超えないよう調整
                    if (newWidth > width)
                    {
                        newWidth = width;
                        newHeight = (int)(width / aspectRatio);
                    }
                    if (newHeight > height)
                    {
                        newHeight = height;
                        newWidth = (int)(height * aspectRatio);
                    }

                    // 高品質なリサイズを実行
                    var resizeInfo = new SKImageInfo(newWidth, newHeight, SKColorType.Bgra8888);
                    using var scaledBitmap = originalBitmap.Resize(resizeInfo, SKFilterQuality.High);
                    using var surface = SKSurface.Create(resizeInfo);

                    if (surface == null || scaledBitmap == null)
                    {
                        return GenerateErrorThumbnail(width, height, "サムネイル作成に失敗しました");
                    }

                    // 背景を白で塗りつぶして描画
                    var canvas = surface.Canvas;
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(scaledBitmap, 0, 0);

                    // SKImageに変換
                    using var skImage = surface.Snapshot();
                    using var data = skImage.Encode(SKEncodedImageFormat.Png, 90);

                    // BitmapSourceに変換
                    using var memoryStream = new MemoryStream();
                    data.SaveTo(memoryStream);
                    memoryStream.Position = 0;

                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = memoryStream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze(); // スレッド間で安全に使用できるようにする

                    return bitmapImage;
                }
                catch (Exception ex)
                {
                    return GenerateErrorThumbnail(width, height, ex.Message);
                }
            });
        }

        /// <summary>
        /// エラー時に表示するサムネイルを生成します
        /// </summary>
        private static BitmapSource GenerateErrorThumbnail(int width, int height, string errorMessage)
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

            // SKImageに変換
            using var skImage = surface.Snapshot();
            using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
            using var memoryStream = new MemoryStream();
            data.SaveTo(memoryStream);
            memoryStream.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = memoryStream;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            return bitmapImage;
        }
    }
}
