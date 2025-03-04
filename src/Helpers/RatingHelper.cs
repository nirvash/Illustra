using System.Windows.Media;

namespace Illustra.Helpers
{
    /// <summary>
    /// レーティングの表示に関するヘルパーメソッドを提供するクラス
    /// </summary>
    public static class RatingHelper
    {
        /// <summary>
        /// レーティング値に対応した色を取得します
        /// </summary>
        /// <param name="rating">レーティング値 (1-5)</param>
        /// <returns>レーティング値に対応した色のブラシ</returns>
        public static Brush GetRatingColor(int rating)
        {
            return rating switch
            {
                1 => Brushes.Red,
                2 => Brushes.Orange,
                3 => Brushes.Gold,
                4 => Brushes.LightGreen,
                5 => Brushes.DeepSkyBlue,
                _ => Brushes.Gray
            };
        }

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
