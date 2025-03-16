using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Illustra.Helpers;

namespace Illustra.Helpers
{
    /// <summary>
    /// 両端キュー（デック）の機能を提供するジェネリッククラス
    /// </summary>
    public class DequeList<T>
    {
        private List<T> list = new List<T>();

        public int Count => list.Count;

        // インデクサー
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= list.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return list[index];
            }
            set
            {
                if (index < 0 || index >= list.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                list[index] = value;
            }
        }

        // 要素の削除
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= list.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            list.RemoveAt(index);
        }

        // 追加
        public void PushFront(T item) => list.Insert(0, item);
        public void PushBack(T item) => list.Add(item);

        // 削除
        public T PopFront()
        {
            if (list.Count == 0) throw new InvalidOperationException("Deque is empty");
            T item = list[0];
            list.RemoveAt(0);
            return item;
        }

        public T PopBack()
        {
            if (list.Count == 0) throw new InvalidOperationException("Deque is empty");
            T item = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return item;
        }

        // 参照
        public T PeekFront()
        {
            if (list.Count == 0) throw new InvalidOperationException("Deque is empty");
            return list[0];
        }

        public T PeekBack()
        {
            if (list.Count == 0) throw new InvalidOperationException("Deque is empty");
            return list[list.Count - 1];
        }

        // 全体アクセス
        public IReadOnlyList<T> Items => list.AsReadOnly();

        // クリア
        public void Clear() => list.Clear();

        // 条件に一致する要素を削除
        public int RemoveAll(Predicate<T> match) => list.RemoveAll(match);
    }

    /// <summary>
    /// サムネイル処理リクエストのキュー管理クラス
    /// </summary>
    public class ThumbnailRequestQueue
    {
        private readonly object _queueLock = new object();
        private ThumbnailRequest? _currentRequest;
        private readonly DequeList<ThumbnailRequest> _highPriorityRequests = new DequeList<ThumbnailRequest>();
        private readonly DequeList<ThumbnailRequest> _normalPriorityRequests = new DequeList<ThumbnailRequest>();
        private bool _isProcessing = false;
        private readonly IThumbnailProcessorService _thumbnailProcessor;
        private bool _isScrolling = false;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="thumbnailProcessor">サムネイル処理サービス</param>
        public ThumbnailRequestQueue(IThumbnailProcessorService thumbnailProcessor)
        {
            _thumbnailProcessor = thumbnailProcessor ?? throw new ArgumentNullException(nameof(thumbnailProcessor));
        }

        /// <summary>
        /// リクエストをキューに追加します
        /// </summary>
        /// <param name="request">サムネイル処理リクエスト</param>
        public void EnqueueRequest(ThumbnailRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            lock (_queueLock)
            {
                // 優先度に基づいてリストを選択
                if (request.IsHighPriority)
                {
                    _highPriorityRequests.PushBack(request);
                    LogHelper.LogWithTimestamp($"高優先度リクエストをキューに追加: {request.StartIndex}～{request.EndIndex}", LogHelper.Categories.ThumbnailQueue);
                }
                else
                {
                    _normalPriorityRequests.PushBack(request);
                    LogHelper.LogWithTimestamp($"通常優先度リクエストをキューに追加: {request.StartIndex}～{request.EndIndex}", LogHelper.Categories.ThumbnailQueue);
                }

                // 処理中でなければ次のリクエストを処理開始
                if (!_isProcessing)
                {
                    ProcessNextRequestIfAvailable();
                }
            }
        }

        private void ProcessNextRequestIfAvailable()
        {
            ThumbnailRequest? nextRequest = null;

            lock (_queueLock)
            {
                // 既に処理中なら何もしない
                if (_isProcessing)
                {
                    return;
                }

                // 高優先度リストから取得を試みる
                if (_highPriorityRequests.Count > 0)
                {
                    nextRequest = _highPriorityRequests.PopFront();
                    LogHelper.LogWithTimestamp($"高優先度キューからリクエストを取得: {nextRequest.StartIndex}～{nextRequest.EndIndex}", LogHelper.Categories.ThumbnailQueue);
                }
                // 高優先度リストが空なら通常リストから取得
                else if (_normalPriorityRequests.Count > 0)
                {
                    nextRequest = _normalPriorityRequests.PopFront();
                    LogHelper.LogWithTimestamp($"通常キューからリクエストを取得: {nextRequest.StartIndex}～{nextRequest.EndIndex}", LogHelper.Categories.ThumbnailQueue);
                }

                // 処理するリクエストがあれば処理開始
                if (nextRequest != null)
                {
                    _currentRequest = nextRequest;
                    _isProcessing = true;
                }
            }

            // ロックの外で非同期処理を実行
            if (nextRequest != null)
            {
                Task.Run(() => ProcessThumbnailRequestAsync(nextRequest));
            }
        }

        private async Task ProcessThumbnailRequestAsync(ThumbnailRequest request)
        {
            LogHelper.LogWithTimestamp($"サムネイル処理実行中: {request.StartIndex}～{request.EndIndex}", LogHelper.Categories.ThumbnailQueue);

            // 初期状態では成功と仮定
            bool success = true;

            try
            {
                // キャンセルチェック
                if (request.CancellationToken.IsCancellationRequested)
                {
                    LogHelper.LogWithTimestamp($"開始前にキャンセルされました: {request.StartIndex}～{request.EndIndex}", LogHelper.Categories.ThumbnailQueue);
                    return;
                }

                // スクロール中の処理制御
                if (_isScrolling && !request.IsHighPriority)
                {
                    // 現在処理中のリクエストと範囲が重なる場合のみ処理を継続
                    if (_currentRequest != null && request.OverlapsWith(_currentRequest))
                    {
                        LogHelper.LogWithTimestamp($"スクロール中ですが表示範囲内のため処理継続: {request.StartIndex}～{request.EndIndex}", LogHelper.Categories.ThumbnailQueue);
                    }
                    else
                    {
                        LogHelper.LogWithTimestamp($"スクロール中かつ表示範囲外のため処理をスキップ: {request.StartIndex}～{request.EndIndex}", LogHelper.Categories.ThumbnailQueue);
                        return;
                    }
                }

                // バッチ処理の実装 - バッチサイズを10に増やす
                int batchSize = 10; // 一度に処理するサムネイルの数を増やす
                for (int i = request.StartIndex; i <= request.EndIndex; i += batchSize)
                {
                    if (request.CancellationToken.IsCancellationRequested)
                    {
                        LogHelper.LogWithTimestamp($"バッチ処理中にキャンセルされました: {i}～{request.EndIndex}", LogHelper.Categories.ThumbnailQueue);
                        return;
                    }

                    int endBatchIndex = Math.Min(i + batchSize - 1, request.EndIndex);

                    // 各バッチの処理を並列化
                    var tasks = new List<Task>();
                    for (int j = i; j <= endBatchIndex; j++)
                    {
                        int index = j; // ローカル変数にキャプチャ
                        if (request.CancellationToken.IsCancellationRequested)
                        {
                            LogHelper.LogWithTimestamp($"サムネイル処理中にキャンセルされました: インデックス {index}", LogHelper.Categories.ThumbnailQueue);
                            return;
                        }

                        // 各サムネイル処理をタスクとして追加
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                await _thumbnailProcessor.CreateThumbnailAsync(index, request.CancellationToken);
                            }
                            catch (OperationCanceledException)
                            {
                                LogHelper.LogWithTimestamp($"サムネイル生成中にキャンセルされました: インデックス {index}", LogHelper.Categories.ThumbnailQueue);
                            }
                            catch (Exception ex)
                            {
                                LogHelper.LogError($"サムネイル処理中にエラーが発生しました (インデックス: {index}): {ex.Message}", ex);
                            }
                        }));
                    }

                    try
                    {
                        // すべてのタスクが完了するまで待機（キャンセル可能）
                        await Task.WhenAll(tasks);
                    }
                    catch (OperationCanceledException)
                    {
                        LogHelper.LogWithTimestamp($"バッチ処理中にキャンセルされました: {i}～{endBatchIndex}", LogHelper.Categories.ThumbnailQueue);
                        return;
                    }
                    catch (Exception ex)
                    {
                        LogHelper.LogError($"バッチ処理中にエラーが発生しました: {ex.Message}", ex);
                        // バッチ処理のエラーは記録するが処理は続行
                    }

                    // バッチ間で短い遅延を入れる（キャンセルチェック付き）
                    if (!request.CancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(5);
                    }
                }

                LogHelper.LogWithTimestamp($"サムネイル処理が完了しました: {request.StartIndex}～{request.EndIndex}", LogHelper.Categories.ThumbnailQueue);
            }
            catch (Exception ex)
            {
                success = false;
                LogHelper.LogError($"サムネイル処理中にエラーが発生しました: {ex.Message}", ex);
            }
            finally
            {
                try
                {
                    request.CompletionCallback?.Invoke(request, success);
                }
                catch (Exception callbackEx)
                {
                    LogHelper.LogError($"コールバック実行中にエラーが発生しました: {callbackEx.Message}", callbackEx);
                }

                lock (_queueLock)
                {
                    _currentRequest = null;
                    _isProcessing = false;
                    ProcessNextRequestIfAvailable();
                }
            }
        }

        /// <summary>
        /// キューの状態を確認します
        /// </summary>
        /// <returns>高優先度キュー数、通常キュー数、処理中フラグのタプル</returns>
        public (int highPriorityCount, int normalPriorityCount, bool isProcessing) GetQueueStatus()
        {
            lock (_queueLock)
            {
                return (_highPriorityRequests.Count, _normalPriorityRequests.Count, _isProcessing);
            }
        }

        /// <summary>
        /// すべてのリクエストをクリアします
        /// </summary>
        public void ClearQueue()
        {
            lock (_queueLock)
            {
                _highPriorityRequests.Clear();
                _normalPriorityRequests.Clear();
                // 注意: 現在処理中のリクエストはキャンセルされません
                LogHelper.LogWithTimestamp("すべてのリクエストがクリアされました", LogHelper.Categories.ThumbnailQueue);
            }
        }

        /// <summary>
        /// 指定されたキャンセルトークンに関連するリクエストをキューから削除します
        /// </summary>
        public void CancelRequests(CancellationToken token)
        {
            lock (_queueLock)
            {
                int highPriorityRemoved = 0;
                int normalPriorityRemoved = 0;

                // 高優先度キューから削除
                for (int i = _highPriorityRequests.Count - 1; i >= 0; i--)
                {
                    if (_highPriorityRequests[i].CancellationToken == token ||
                        _highPriorityRequests[i].CancellationToken.IsCancellationRequested)
                    {
                        _highPriorityRequests.RemoveAt(i);
                        highPriorityRemoved++;
                    }
                }

                // 通常優先度キューから削除
                for (int i = _normalPriorityRequests.Count - 1; i >= 0; i--)
                {
                    if (_normalPriorityRequests[i].CancellationToken == token ||
                        _normalPriorityRequests[i].CancellationToken.IsCancellationRequested)
                    {
                        _normalPriorityRequests.RemoveAt(i);
                        normalPriorityRemoved++;
                    }
                }

                if (highPriorityRemoved > 0 || normalPriorityRemoved > 0)
                {
                    LogHelper.LogWithTimestamp($"キャンセルされたリクエストをキューから削除しました: 高優先度={highPriorityRemoved}件, 通常優先度={normalPriorityRemoved}件",
                        LogHelper.Categories.ThumbnailQueue);
                }
            }
        }

        /// <summary>
        /// 重複するリクエストを最適化します
        /// </summary>
        public void OptimizeRequests()
        {
            lock (_queueLock)
            {
                // 重複する範囲のリクエストをマージするなどの最適化処理
                // 実装例：同じ範囲をカバーする複数のリクエストを1つにまとめる

                // 高優先度リクエストの最適化
                OptimizeRequestList(_highPriorityRequests);

                // 通常優先度リクエストの最適化
                OptimizeRequestList(_normalPriorityRequests);

                LogHelper.LogWithTimestamp("リクエストキューを最適化しました", LogHelper.Categories.ThumbnailQueue);
            }
        }

        /// <summary>
        /// リクエストリストを最適化します
        /// </summary>
        private void OptimizeRequestList(DequeList<ThumbnailRequest> requests)
        {
            if (requests.Count <= 1)
                return;

            // リクエストをリストに変換して処理
            var requestList = requests.Items.ToList();
            var optimizedRequests = new List<ThumbnailRequest>();

            // リクエストをソート（開始インデックスの昇順）
            requestList.Sort((a, b) => a.StartIndex.CompareTo(b.StartIndex));

            // 重複するリクエストをマージ
            ThumbnailRequest current = requestList[0];
            for (int i = 1; i < requestList.Count; i++)
            {
                var next = requestList[i];

                // 現在のリクエストが次のリクエストと重複または隣接している場合
                if (next.StartIndex <= current.EndIndex + 1)
                {
                    // 範囲を拡張
                    current = new ThumbnailRequest(
                        startIndex: Math.Min(current.StartIndex, next.StartIndex),
                        endIndex: Math.Max(current.EndIndex, next.EndIndex),
                        isHighPriority: current.IsHighPriority || next.IsHighPriority,
                        cancellationToken: current.CancellationToken,
                        completionCallback: current.CompletionCallback
                    );
                }
                else
                {
                    // 重複していない場合は現在のリクエストを追加して次へ
                    optimizedRequests.Add(current);
                    current = next;
                }
            }

            // 最後のリクエストを追加
            optimizedRequests.Add(current);

            // 元のリストをクリアして最適化されたリクエストを追加
            requests.Clear();
            foreach (var request in optimizedRequests)
            {
                requests.PushBack(request);
            }

            LogHelper.LogWithTimestamp($"リクエストを最適化しました: {requestList.Count}件 → {optimizedRequests.Count}件", LogHelper.Categories.ThumbnailQueue);
        }

        /// <summary>
        /// スクロール中かどうかを設定します
        /// </summary>
        public void SetScrolling(bool isScrolling)
        {
            lock (_queueLock)
            {
                _isScrolling = isScrolling;

                // スクロール中になった場合、現在のリクエストと重ならない通常優先度リクエストをクリア
                if (isScrolling && _currentRequest != null)
                {
                    int removedCount = 0;
                    for (int i = _normalPriorityRequests.Count - 1; i >= 0; i--)
                    {
                        var request = _normalPriorityRequests[i];
                        // 現在のリクエストと範囲が重ならないものだけを削除
                        if (!request.OverlapsWith(_currentRequest))
                        {
                            _normalPriorityRequests.RemoveAt(i);
                            removedCount++;
                        }
                    }

                    if (removedCount > 0)
                    {
                        LogHelper.LogWithTimestamp($"スクロール中のため表示範囲外の{removedCount}件のリクエストをクリア", LogHelper.Categories.ThumbnailQueue);
                    }
                }
            }
        }
    }
}
