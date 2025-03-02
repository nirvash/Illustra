namespace Illustra.Events
{
    /// <summary>
    /// フォルダが選択されたときにトリガーされるイベント
    public class FolderSelectedEvent : PubSubEvent<string> { }

    /// <summary>
    /// フォルダ内の先頭ファイルの選択を要求するイベント
    public class SelectFolderFirstItemRequestEvent : PubSubEvent { }
}
