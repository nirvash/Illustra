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

        /// <summary>通知元。主ウィンドウのサムネイル選択だけをステータスバーへ反映するために使用。</summary>
        public string SourceId { get; }

        /// <summary>通知元で単一選択されているファイルのパス。ステータス表示用。</summary>
        public string? SelectedFilePath { get; }

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="selectedCount">選択されているアイテムの数。</param>
        public SelectionCountChangedEventArgs(
            int selectedCount,
            string sourceId = "ThumbnailList",
            string? selectedFilePath = null)
        {
            SelectedCount = selectedCount;
            SourceId = sourceId;
            SelectedFilePath = selectedFilePath;
        }
    }

    /// <summary>
    /// ThumbnailListControl で選択されているアイテム数が変更されたときに発行されるイベント。
    /// </summary>
    public class SelectionCountChangedEvent : PubSubEvent<SelectionCountChangedEventArgs> { }
}
