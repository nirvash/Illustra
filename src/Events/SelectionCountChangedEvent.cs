using Prism.Events;

namespace Illustra.Events
{
    /// <summary>
    /// ThumbnailListControl で選択されているアイテム数を通知するためのイベント引数。
    /// </summary>
    public class SelectionCountChangedEventArgs
    {
        /// <summary>
        /// 選択されているアイテムの数。
        /// </summary>
        public int SelectedCount { get; }

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="selectedCount">選択されているアイテムの数。</param>
        public SelectionCountChangedEventArgs(int selectedCount)
        {
            SelectedCount = selectedCount;
        }
    }

    /// <summary>
    /// ThumbnailListControl で選択されているアイテム数が変更されたときに発行されるイベント。
    /// </summary>
    public class SelectionCountChangedEvent : PubSubEvent<SelectionCountChangedEventArgs> { }
}
