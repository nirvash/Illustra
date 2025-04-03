using Prism.Events;

namespace Illustra.Events
{
    /// <summary>
    /// 新しいタブでフォルダを開くよう要求するイベントの引数
    /// </summary>
    public class OpenInNewTabEventArgs
    {
        /// <summary>
        /// 開くフォルダのパス
        /// </summary>
        public string FolderPath { get; }

        /// <summary>
        /// イベントを発行したコントロールのID
        /// </summary>
        public string SourceId { get; }

        public OpenInNewTabEventArgs(string folderPath, string sourceId)
        {
            FolderPath = folderPath;
            SourceId = sourceId;
        }
    }

    /// <summary>
    /// 新しいタブでフォルダを開くよう要求するイベント
    /// </summary>
    public class OpenInNewTabEvent : PubSubEvent<OpenInNewTabEventArgs>
    {
    }
}
