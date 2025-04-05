using System.Collections.Generic;

namespace Illustra.Models
{
    /// <summary>
    /// サムネイルリストのフィルタ設定を保持するクラス
    /// </summary>
    public class FilterSettings
    {
        /// <summary>
        /// レーティングフィルタの値 (0はフィルタなし)
        /// </summary>
        public int Rating { get; set; } = 0;

        /// <summary>
        /// プロンプト有無フィルタが有効か
        /// </summary>
        public bool HasPrompt { get; set; } = false;

        /// <summary>
        /// タグフィルタのリスト (AND条件)
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// 拡張子フィルタのリスト (OR条件)
        /// </summary>
        public List<string> Extensions { get; set; } = new List<string>();

        /// <summary>
        /// いずれかのフィルタが有効かどうか
        /// </summary>
        public bool IsAnyFilterActive => Rating > 0 || HasPrompt || Tags.Count > 0 || Extensions.Count > 0;

        /// <summary>
        /// デフォルトのフィルタ設定かどうか
        /// </summary>
        public bool IsDefault => Rating == 0 && !HasPrompt && Tags.Count == 0 && Extensions.Count == 0;

        /// <summary>
        /// フィルタ設定をクリアします。
        /// </summary>
        public void Clear()
        {
            Rating = 0;
            HasPrompt = false;
            Tags.Clear();
            Extensions.Clear();
        }

        /// <summary>
        /// 別の FilterSettings インスタンスから値をコピーします。
        /// </summary>
        public void CopyFrom(FilterSettings source)
        {
            Rating = source.Rating;
            HasPrompt = source.HasPrompt;
            Tags = new List<string>(source.Tags); // 新しいリストを作成してコピー
            Extensions = new List<string>(source.Extensions); // 新しいリストを作成してコピー
        }

        /// <summary>
        /// 現在のインスタンスのディープコピーを作成します。
        /// </summary>
        public FilterSettings Clone()
        {
            var clone = new FilterSettings
            {
                Rating = this.Rating,
                HasPrompt = this.HasPrompt,
                Tags = new List<string>(this.Tags), // 新しいリストを作成してコピー
                Extensions = new List<string>(this.Extensions) // 新しいリストを作成してコピー
            };
            return clone;
        }
    }
}
