namespace Illustra.Models
{
    /// <summary>
    /// サムネイルリストのソート設定を保持するクラス
    /// </summary>
    public class SortSettings
    {
        /// <summary>
        /// 日付でソートするかどうか (falseの場合はファイル名でソート)
        /// </summary>
        public bool SortByDate { get; set; } = true; // デフォルトは日付順

        /// <summary>
        /// 昇順でソートするかどうか
        /// </summary>
        public bool SortAscending { get; set; } = true; // デフォルトは昇順

        /// <summary>
        /// デフォルトのソート設定かどうか
        /// </summary>
        public bool IsDefault => SortByDate && SortAscending;

        /// <summary>
        /// 別の SortSettings インスタンスから値をコピーします。
        /// </summary>
        public void CopyFrom(SortSettings source)
        {
            SortByDate = source.SortByDate;
            SortAscending = source.SortAscending;
        }

        /// <summary>
        /// 現在のインスタンスのディープコピーを作成します。
        /// </summary>
        public SortSettings Clone()
        {
            return new SortSettings
            {
                SortByDate = this.SortByDate,
                SortAscending = this.SortAscending
            };
        }
    }
}
