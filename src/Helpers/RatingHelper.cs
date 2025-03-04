using System.Windows.Media;

namespace Illustra.Helpers
{
    /// <summary>
    /// レーティングの表示に関するヘルパーメソッドを提供するクラス
    /// </summary>
    public static class RatingHelper
    {
        public enum RatingTheme
        {
            GoldOrange,
            Blue,
            Green,
            Red,
            Colorful
        }
        /// <summary>
        /// レーティング値に対応した色を取得します
        /// </summary>
        /// <param name="rating">レーティング値 (1-5)</param>
        /// <returns>レーティング値に対応した色のブラシ</returns>
        public static Brush GetRatingColor(int rating, RatingTheme theme = RatingTheme.Colorful)
        {
            return theme switch
            {
                RatingTheme.GoldOrange => rating switch
                {
                    1 => new SolidColorBrush(Color.FromRgb(189, 189, 189)), // Light Gray
                    2 => new SolidColorBrush(Color.FromRgb(251, 192, 45)),  // Smoky Yellow
                    3 => new SolidColorBrush(Color.FromRgb(249, 168, 37)),  // Gold
                    4 => new SolidColorBrush(Color.FromRgb(245, 127, 23)),  // Deep Gold
                    5 => new SolidColorBrush(Color.FromRgb(230, 81, 0)),    // Dark Orange
                    _ => Brushes.Gray
                },

                RatingTheme.Blue => rating switch
                {
                    1 => new SolidColorBrush(Color.FromRgb(176, 190, 197)), // Light Blue Gray
                    2 => new SolidColorBrush(Color.FromRgb(144, 202, 249)), // Sky Blue
                    3 => new SolidColorBrush(Color.FromRgb(66, 165, 245)),  // Soft Blue
                    4 => new SolidColorBrush(Color.FromRgb(30, 136, 229)),  // Deep Blue
                    5 => new SolidColorBrush(Color.FromRgb(13, 71, 161)),   // Dark Navy
                    _ => Brushes.Gray
                },

                RatingTheme.Green => rating switch
                {
                    1 => new SolidColorBrush(Color.FromRgb(200, 230, 201)), // Pale Green
                    2 => new SolidColorBrush(Color.FromRgb(165, 214, 167)), // Soft Green
                    3 => new SolidColorBrush(Color.FromRgb(102, 187, 106)), // Lime Green
                    4 => new SolidColorBrush(Color.FromRgb(56, 142, 60)),   // Deep Green
                    5 => new SolidColorBrush(Color.FromRgb(27, 94, 32)),    // Forest Green
                    _ => Brushes.Gray
                },

                RatingTheme.Red => rating switch
                {
                    1 => new SolidColorBrush(Color.FromRgb(255, 205, 210)), // Light Pink
                    2 => new SolidColorBrush(Color.FromRgb(239, 154, 154)), // Soft Red
                    3 => new SolidColorBrush(Color.FromRgb(229, 115, 115)), // Moderate Red
                    4 => new SolidColorBrush(Color.FromRgb(211, 47, 47)),   // Deep Red
                    5 => new SolidColorBrush(Color.FromRgb(183, 28, 28)),   // Dark Red
                    _ => Brushes.Gray
                },

                RatingTheme.Colorful => rating switch
                {
                    1 => new SolidColorBrush(Color.FromRgb(255, 205, 210)), // Light Pink
                    2 => new SolidColorBrush(Color.FromRgb(165, 214, 167)), // Soft Green
                    3 => new SolidColorBrush(Color.FromRgb(66, 165, 245)),  // Soft Blue
                    4 => new SolidColorBrush(Color.FromRgb(56, 142, 60)),   // Deep Green
                    5 => new SolidColorBrush(Color.FromRgb(230, 81, 0)),    // Dark Orange
                    _ => Brushes.Gray
                },

                _ => Brushes.Gray
            };
        }

        /// <summary>
        /// レーティング値とテーマに応じたテキスト色を取得します
        /// </summary>
        /// <param name="rating">レーティング値 (1-5)</param>
        /// <param name="theme">テーマ</param>
        /// <returns>テキスト色のブラシ</returns>
        public static SolidColorBrush GetTextColor(int rating = 0, RatingTheme theme = RatingTheme.Colorful)
        {
            // レーティングが0の場合はデフォルト色を返す
            if (rating <= 0)
            {
                return new SolidColorBrush(Color.FromRgb(96, 96, 96)); // デフォルトのダークグレー
            }

            // レーティング値に応じて暗さを調整 (レーティングが低いほど暗い)
            double brightness = rating > 3 ? 1.0 : 0.2 + (rating * 0.1);

            // テーマ別の基本色を取得
            Color baseColor = theme switch
            {
                RatingTheme.GoldOrange => Color.FromRgb(255, 248, 225), // 薄い金色
                RatingTheme.Blue => Color.FromRgb(187, 222, 251),       // 薄い青
                RatingTheme.Green => Color.FromRgb(200, 230, 201),      // 薄い緑
                RatingTheme.Red => Color.FromRgb(255, 205, 210),        // 薄いピンク
                _ => Color.FromRgb(245, 245, 245)                       // 薄いグレー
            };

            // 明るさを調整した新しい色を作成
            byte r = (byte)(baseColor.R * brightness);
            byte g = (byte)(baseColor.G * brightness);
            byte b = (byte)(baseColor.B * brightness);

            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        // 後方互換性のためのプロパティ
        public static SolidColorBrush TextColor => new SolidColorBrush(Color.FromRgb(96, 96, 96)); // DarkGrayに相当

        /// <summary>
        /// レーティング値に対応した星マークの文字列を取得します
        /// </summary>
        /// <param name="rating">レーティング値 (1-5)</param>
        /// <returns>レーティング値に対応した星マークの文字列</returns>
        public static string GetRatingStars(int rating)
        {
            return rating switch
            {
                1 => "★☆☆☆☆",
                2 => "★★☆☆☆",
                3 => "★★★☆☆",
                4 => "★★★★☆",
                5 => "★★★★★",
                _ => "☆☆☆☆☆"
            };
        }

        /// <summary>
        /// サムネイル表示用の数字入りレーティングスターを取得します
        /// </summary>
        /// <param name="rating">レーティング値 (1-5)</param>
        /// <returns>数字入りの星マーク</returns>
        public static string GetRatingStarWithNumber(int rating)
        {
            if (rating <= 0 || rating > 5)
                return "";

            // Unicode文字で★に数字を近似的に表現
            return $"★{rating}";
        }

        /// <summary>
        /// 単一のレーティング位置に対応する星マークを取得します（選択用）
        /// </summary>
        /// <param name="position">星の位置 (1-5)</param>
        /// <param name="rating">現在のレーティング値 (0-5)</param>
        /// <returns>対応する星マーク（塗りつぶしまたは輪郭）</returns>
        public static string GetStarAtPosition(int position, int rating)
        {
            return position <= rating ? "★" : "☆";
        }
    }
}
