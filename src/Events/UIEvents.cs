namespace Illustra.Events
{
    /// <summary>
    /// フォルダが選択されたときにトリガーされるイベント
    /// </summary>
    public class FolderSelectedEvent : PubSubEvent<string> { }

    /// <summary>
    /// フォルダ内の先頭ファイルの選択を要求するイベント
    /// </summary>
    public class SelectFolderFirstItemRequestEvent : PubSubEvent { }

    /// <summary>
    /// ファイルが選択されたときにトリガーされるイベント
    /// </summary>
    public class FileSelectedEvent : PubSubEvent<string> { }
}
