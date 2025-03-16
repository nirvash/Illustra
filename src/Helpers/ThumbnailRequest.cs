using System;
using System.Threading;

namespace Illustra.Helpers
{
    /// <summary>
    /// サムネイル処理リクエストを表すクラス
    /// </summary>
    public class ThumbnailRequest
    {
        /// <summary>
        /// 処理開始インデックス
        /// </summary>
        public int StartIndex { get; }

        /// <summary>
        /// 処理終了インデックス
        /// </summary>
        public int EndIndex { get; }

        /// <summary>
        /// 高優先度フラグ
        /// </summary>
        public bool IsHighPriority { get; }

        /// <summary>
        /// キャンセルトークン
        /// </summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// 完了時コールバック
        /// </summary>
        public Action<ThumbnailRequest, bool>? CompletionCallback { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public ThumbnailRequest(
            int startIndex,
            int endIndex,
            bool isHighPriority,
            CancellationToken cancellationToken,
            Action<ThumbnailRequest, bool>? completionCallback = null)
        {
            StartIndex = startIndex;
            EndIndex = endIndex;
            IsHighPriority = isHighPriority;
            CancellationToken = cancellationToken;
            CompletionCallback = completionCallback;
        }

        /// <summary>
        /// 文字列表現を返します
        /// </summary>
        public override string ToString()
        {
            return $"ThumbnailRequest[{StartIndex}-{EndIndex}, Priority:{(IsHighPriority ? "高" : "通常")}]";
        }

        // 範囲が重複しているかチェック
        public bool OverlapsWith(ThumbnailRequest other)
        {
            return !(EndIndex < other.StartIndex || StartIndex > other.EndIndex);
        }

        // 指定されたインデックスが範囲内にあるかチェック
        public bool Contains(int index)
        {
            return index >= StartIndex && index <= EndIndex;
        }
    }
}