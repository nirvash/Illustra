using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Illustra.Models
{
    /// <summary>
    /// 大量のアイテムを効率的に一括追加できるObservableCollectionの拡張クラス
    /// </summary>
    /// <typeparam name="T">コレクションの要素の型</typeparam>
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        /// <summary>
        /// 複数のアイテムを一括で追加します。通知は最後に一度だけ発行されます。
        /// </summary>
        /// <param name="items">追加するアイテムのコレクション</param>
        public void AddRange(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            this.CheckReentrancy();  // UI の競合を防ぐ

            foreach (var item in items)
            {
                Items.Add(item); // `Items` を直接操作（イベント発火を防ぐ）
            }

            // まとめて `CollectionChanged` イベントを発火（リスト全体を更新）
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>
        /// コレクションを一括でクリアして新しいアイテムを追加します。
        /// </summary>
        /// <param name="items">追加するアイテムのコレクション</param>
        public void ReplaceAll(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            this.CheckReentrancy();  // UI の競合を防ぐ

            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            // まとめて `CollectionChanged` イベントを発火（リスト全体を更新）
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
