using System;
using System.Collections.Generic;

namespace Illustra.Helpers
{
    /// <summary>
    /// Least Recently Used (LRU) キャッシュを実装したジェネリッククラス
    /// </summary>
    public class LruCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly LinkedList<TKey> _lruList;
        private readonly Dictionary<TKey, (LinkedListNode<TKey> Node, TValue Value)> _cache;

        public LruCache(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("キャッシュ容量は1以上である必要があります", nameof(capacity));

            _capacity = capacity;
            _lruList = new LinkedList<TKey>();
            _cache = new Dictionary<TKey, (LinkedListNode<TKey>, TValue)>();
        }

        /// <summary>
        /// キャッシュからアイテムを取得します。アクセスされたアイテムは最新として扱われます。
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                // アクセス時に順序を更新
                _lruList.Remove(entry.Node);
                _lruList.AddFirst(entry.Node);
                value = entry.Value;
                return true;
            }
            value = default!;
            return false;
        }

        /// <summary>
        /// キャッシュにアイテムを追加します。キャッシュが一杯の場合、最も古いアイテムが削除されます。
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            if (_cache.TryGetValue(key, out var existingEntry))
            {
                // 既存エントリの更新
                _lruList.Remove(existingEntry.Node);
                _cache.Remove(key);
            }
            else if (_cache.Count >= _capacity)
            {
                // 最も古いエントリを削除
                var lastNode = _lruList.Last;
                _cache.Remove(lastNode!.Value);
                _lruList.RemoveLast();
            }

            // 新しいエントリを追加
            var node = _lruList.AddFirst(key);
            _cache.Add(key, (node, value));
        }

        /// <summary>
        /// キャッシュをクリアします
        /// </summary>
        public void Clear()
        {
            _lruList.Clear();
            _cache.Clear();
        }

        /// <summary>
        /// キャッシュに含まれるキーをアクセス順（新→古）で返します
        /// </summary>
        public IEnumerable<TKey> GetKeys() => _lruList;

        /// <summary>
        /// 現在のキャッシュサイズを取得します
        /// </summary>
        public int Count => _cache.Count;

        /// <summary>
        /// キャッシュの最大容量を取得します
        /// </summary>
        public int Capacity => _capacity;
    }
}
