using Prism.Events;

namespace Illustra.Events
{
    public class FolderSelectedEventArgs
    {
        public string Path { get; set; }
        public string SourceId { get; set; }

        public FolderSelectedEventArgs(string path, string sourceId)
        {
            Path = path;
            SourceId = sourceId;
        }
    }

    /// <summary>
    /// フォルダが選択されたときにトリガーされるイベント
    /// </summary>
    public class FolderSelectedEvent : PubSubEvent<FolderSelectedEventArgs> { }

    /// <summary>
    /// フォルダ内の先頭ファイルの選択を要求するイベント
    /// </summary>
    public class SelectFileRequestEvent : PubSubEvent<string> { }

    /// <summary>
    /// ファイルが選択されたときにトリガーされるイベント
    /// </summary>
    public class FileSelectedEvent : PubSubEvent<string> { }

    /// <summary>
    /// お気に入りに追加するイベント
    /// </summary>
    public class AddToFavoritesEvent : PubSubEvent<string> { }

    /// <summary>
    /// お気に入りから削除するイベント
    /// </summary>
    public class RemoveFromFavoritesEvent : PubSubEvent<string> { }

    /// <summary>
    /// レーティング変更イベントの引数
    /// </summary>
    public class RatingChangedEventArgs
    {
        public string FilePath { get; set; } = string.Empty;
        public int Rating { get; set; }
    }

    /// <summary>
    /// レーティングが変更されたときにトリガーされるイベント
    /// </summary>
    public class RatingChangedEvent : PubSubEvent<RatingChangedEventArgs> { }
}
