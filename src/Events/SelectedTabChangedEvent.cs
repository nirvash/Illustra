using Illustra.Models; // TabState を使用するために追加
using Prism.Events;

namespace Illustra.Events
{
    /// <summary>
    /// 選択されたタブが変更されたことを通知するイベントの引数
    /// </summary>
    public class SelectedTabChangedEventArgs
    {
        /// <summary>
        /// 新しく選択されたタブの状態 (null の場合あり)
        /// </summary>
        public TabState? NewTabState { get; }

        public SelectedTabChangedEventArgs(TabState? newTabState)
        {
            NewTabState = newTabState;
        }
    }

    /// <summary>
    /// 選択されたタブが変更されたことを通知するイベント
    /// </summary>
    public class SelectedTabChangedEvent : PubSubEvent<SelectedTabChangedEventArgs>
    {
    }
}
