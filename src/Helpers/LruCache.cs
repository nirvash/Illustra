using System;
using System.Collections.Generic;
using System.Linq;

namespace Illustra.Helpers
{
    /// <summary>
    /// Least Recently Used (LRU) キャッシュの実装
    /// </summary>
    /// <typeparam name="TKey">キャッシュのキーの型</typeparam>
    /// <typeparam name="TValue">キャッシュの値の型</typeparam>
    public class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cacheMap;
        private readonly LinkedList<CacheItem> _lruList;

        /// <summary>
        /// キャッシュの容量
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// 現在のキャッシュ内のアイテム数
        /// </summary>
        public int Count => _cacheMap.Count;

        /// <summary>
        /// キャッシュアイテムを表す内部クラス
        /// </summary>
        private class CacheItem
        {
            public TKey Key { get; }
            public TValue Value { get; }

            public CacheItem(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
        }

        /// <summary>
        /// 指定された容量でLRUキャッシュを初期化します
        /// </summary>
        /// <param name="capacity">キャッシュの最大容量</param>
        public LruCache(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "キャッシュ容量は1以上である必要があります");

            _capacity = capacity;
            _cacheMap = new Dictionary<TKey, LinkedListNode<CacheItem>>(capacity);
            _lruList = new LinkedList<CacheItem>();
        }

        /// <summary>
        /// キャッシュにアイテムを追加または更新します
        /// </summary>
        /// <param name="key">キャッシュのキー</param>
        /// <param name="value">キャッシュの値</param>
        public void Add(TKey key, TValue value)
        {
            // キーが既に存在する場合は、既存のノードを削除
            if (_cacheMap.TryGetValue(key, out var existingNode))
            {
                _lruList.Remove(existingNode);
                _cacheMap.Remove(key);
            }
            // キャッシュが容量に達している場合は、最も古いアイテムを削除
            else if (_cacheMap.Count >= _capacity)
            {
                RemoveLeastRecentlyUsed();
            }

            // 新しいアイテムを先頭に追加
            var cacheItem = new CacheItem(key, value);
            var newNode = _lruList.AddFirst(cacheItem);
            _cacheMap.Add(key, newNode);
        }

        /// <summary>
        /// キャッシュから値を取得します。アクセス時にLRUの順序が更新されます。
        /// </summary>
        /// <param name="key">キャッシュのキー</param>
        /// <param name="value">取得された値</param>
        /// <returns>キーが存在する場合はtrue、それ以外はfalse</returns>
        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_cacheMap.TryGetValue(key, out var node))
            {
                // アクセスされたアイテムを最近使用されたものとして先頭に移動
                _lruList.Remove(node);
                _lruList.AddFirst(node);

                value = node.Value.Value;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// キーがキャッシュに存在するかどうかを確認します。LRUの順序は更新されません。
        /// </summary>
        /// <param name="key">確認するキー</param>
        /// <returns>キーが存在する場合はtrue、それ以外はfalse</returns>
        public bool ContainsKey(TKey key)
        {
            return _cacheMap.ContainsKey(key);
        }

        /// <summary>
        /// キャッシュから値を取得します。LRUの順序は更新されません。
        /// </summary>
        /// <param name="key">キャッシュのキー</param>
        /// <param name="value">取得された値</param>
        /// <returns>キーが存在する場合はtrue、それ以外はfalse</returns>
        public bool TryPeek(TKey key, out TValue value)
        {
            if (_cacheMap.TryGetValue(key, out var node))
            {
                value = node.Value.Value;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// キャッシュからアイテムを取得します（インデクサー）。アクセス時にLRUの順序が更新されます。
        /// </summary>
        /// <param name="key">キャッシュのキー</param>
        /// <returns>キャッシュの値</returns>
        public TValue this[TKey key]
        {
            get
            {
                if (_cacheMap.TryGetValue(key, out var node))
                {
                    // アクセスされたアイテムを最近使用されたものとして先頭に移動
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);

                    return node.Value.Value;
                }

                throw new KeyNotFoundException($"キー '{key}' はキャッシュに存在しません");
            }
        }

        /// <summary>
        /// キャッシュをクリアします
        /// </summary>
        public void Clear()
        {
            _cacheMap.Clear();
            _lruList.Clear();
        }

        /// <summary>
        /// 最も古く使用されたアイテムを削除します
        /// </summary>
        private void RemoveLeastRecentlyUsed()
        {
            var oldest = _lruList.Last;
            if (oldest != null)
            {
                _lruList.RemoveLast();
                _cacheMap.Remove(oldest.Value.Key);
            }
        }

        /// <summary>
        /// キャッシュ内のキーを最近使用された順に取得します
        /// </summary>
        /// <returns>キーのコレクション</returns>
        public IEnumerable<TKey> GetKeys()
        {
            return _lruList.Select(item => item.Key);
        }
    }
}
