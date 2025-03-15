using System;
using System.Collections.Generic;
using System.Linq;
using Illustra.Models;

namespace Illustra.Services
{
    /// <summary>
    /// キャッシュのキーを表すクラス
    /// </summary>
    public class CacheKey : IEquatable<CacheKey>
    {
        public string OperationType { get; }
        public string Parameters { get; }

        public CacheKey(string operationType, string parameters)
        {
            OperationType = operationType;
            Parameters = parameters;
        }

        public bool Equals(CacheKey? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return OperationType == other.OperationType && Parameters == other.Parameters;
        }

        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((CacheKey)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(OperationType, Parameters);
        }
    }

    /// <summary>
    /// キャッシュエントリを表すクラス
    /// </summary>
    /// <typeparam name="T">キャッシュするデータの型</typeparam>
    public class CacheEntry<T>
    {
        public T Data { get; }
        public DateTime CreatedAt { get; }
        public DateTime LastAccessed { get; private set; }
        public int AccessCount { get; private set; }

        public CacheEntry(T data)
        {
            Data = data;
            CreatedAt = DateTime.Now;
            LastAccessed = CreatedAt;
            AccessCount = 0;
        }

        public void UpdateAccess()
        {
            LastAccessed = DateTime.Now;
            AccessCount++;
        }
    }

    /// <summary>
    /// 操作結果のキャッシュを管理するクラス
    /// </summary>
    public class OperationCache
    {
        private readonly Dictionary<string, Func<Task<IList<FileNodeModel>>>> _operations = new();

        public int Count => _operations.Count;

        public void AddOperation(string key, Func<Task<IList<FileNodeModel>>> operation)
        {
            _operations[key] = operation;
        }

        public Func<Task<IList<FileNodeModel>>>? GetOperation(string key)
        {
            _operations.TryGetValue(key, out var operation);
            return operation;
        }

        public void RemoveOperation(string key)
        {
            _operations.Remove(key);
        }

        public void Clear()
        {
            _operations.Clear();
        }

        private readonly Dictionary<CacheKey, CacheEntry<object>> _cache = new();
        private readonly int _maxCacheSize;
        private readonly TimeSpan _cacheExpiration;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="maxCacheSize">最大キャッシュサイズ</param>
        /// <param name="cacheExpirationMinutes">キャッシュの有効期限（分）</param>
        public OperationCache(int maxCacheSize = 100, int cacheExpirationMinutes = 30)
        {
            _maxCacheSize = maxCacheSize;
            _cacheExpiration = TimeSpan.FromMinutes(cacheExpirationMinutes);
        }

        /// <summary>
        /// キャッシュからデータを取得
        /// </summary>
        /// <typeparam name="T">データの型</typeparam>
        /// <param name="operationType">操作の種類</param>
        /// <param name="parameters">操作のパラメータ</param>
        /// <returns>キャッシュされたデータ、存在しない場合はdefault</returns>
        public T? GetFromCache<T>(string operationType, string parameters)
        {
            var key = new CacheKey(operationType, parameters);

            if (_cache.TryGetValue(key, out var entry))
            {
                // 有効期限切れのチェック
                if (DateTime.Now - entry.LastAccessed > _cacheExpiration)
                {
                    _cache.Remove(key);
                    return default;
                }

                entry.UpdateAccess();
                return (T)entry.Data;
            }

            return default;
        }

        /// <summary>
        /// データをキャッシュに追加
        /// </summary>
        /// <typeparam name="T">データの型</typeparam>
        /// <param name="operationType">操作の種類</param>
        /// <param name="parameters">操作のパラメータ</param>
        /// <param name="data">キャッシュするデータ</param>
        public void AddToCache<T>(string operationType, string parameters, T data)
        {
            var key = new CacheKey(operationType, parameters);

            // キャッシュサイズのチェック
            if (_cache.Count >= _maxCacheSize)
            {
                // 最も古いエントリを削除
                var oldestKey = _cache
                    .OrderBy(x => x.Value.LastAccessed)
                    .First().Key;

                _cache.Remove(oldestKey);
            }

            _cache[key] = new CacheEntry<object>(data!);
        }

        /// <summary>
        /// キャッシュから特定の操作に関するエントリを削除
        /// </summary>
        /// <param name="operationType">操作の種類</param>
        public void InvalidateCache(string operationType)
        {
            var keysToRemove = _cache.Keys
                .Where(k => k.OperationType == operationType)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }
        }

        /// <summary>
        /// キャッシュをすべてクリア
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
        }

        /// <summary>
        /// 有効期限切れのキャッシュエントリを削除
        /// </summary>
        public void CleanupExpiredEntries()
        {
            var now = DateTime.Now;
            var keysToRemove = _cache
                .Where(x => now - x.Value.LastAccessed > _cacheExpiration)
                .Select(x => x.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }
        }

        /// <summary>
        /// キャッシュのプロンプト情報
        /// </summary>
        public Dictionary<string, bool> PromptCache { get; } = new();

        /// <summary>
        /// キャッシュのタグ情報
        /// </summary>
        public Dictionary<string, List<string>> TagCache { get; } = new();

        /// <summary>
        /// プロンプトキャッシュを更新
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="hasPrompt">プロンプトの有無</param>
        public void UpdatePromptCache(string filePath, bool hasPrompt)
        {
            PromptCache[filePath] = hasPrompt;
        }

        /// <summary>
        /// タグキャッシュを更新
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="tags">タグのリスト</param>
        public void UpdateTagCache(string filePath, List<string> tags)
        {
            TagCache[filePath] = tags;
        }

        /// <summary>
        /// フィルタ結果をキャッシュ
        /// </summary>
        /// <param name="ratingFilter">レーティングフィルタ</param>
        /// <param name="result">フィルタ結果</param>
        public void CacheFilterResult(int ratingFilter, List<FileNodeModel> result)
        {
            AddToCache("Filter", $"Rating:{ratingFilter}", result);
        }

        /// <summary>
        /// キャッシュからフィルタ結果を取得
        /// </summary>
        /// <param name="ratingFilter">レーティングフィルタ</param>
        /// <returns>キャッシュされたフィルタ結果、存在しない場合はnull</returns>
        public List<FileNodeModel>? GetCachedFilterResult(int ratingFilter)
        {
            return GetFromCache<List<FileNodeModel>>("Filter", $"Rating:{ratingFilter}");
        }

        /// <summary>
        /// ソート結果をキャッシュ
        /// </summary>
        /// <param name="sortByDate">日付でソートするかどうか</param>
        /// <param name="sortAscending">昇順でソートするかどうか</param>
        /// <param name="result">ソート結果</param>
        public void CacheSortResult(bool sortByDate, bool sortAscending, List<FileNodeModel> result)
        {
            AddToCache("Sort", $"ByDate:{sortByDate},Ascending:{sortAscending}", result);
        }

        /// <summary>
        /// キャッシュからソート結果を取得
        /// </summary>
        /// <param name="sortByDate">日付でソートするかどうか</param>
        /// <param name="sortAscending">昇順でソートするかどうか</param>
        /// <returns>キャッシュされたソート結果、存在しない場合はnull</returns>
        public List<FileNodeModel>? GetCachedSortResult(bool sortByDate, bool sortAscending)
        {
            return GetFromCache<List<FileNodeModel>>("Sort", $"ByDate:{sortByDate},Ascending:{sortAscending}");
        }
    }
}
