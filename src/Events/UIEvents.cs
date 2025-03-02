namespace Illustra.Events
{
    /// <summary>
    /// フォルダが選択されたときにトリガーされるイベントを表します。
    /// </summary>
    /// <param name="string">選択されたフォルダのパスを表す文字列。</param>
    /// </summary>
    public class FolderSelectedEvent : PubSubEvent<string> { }
}
