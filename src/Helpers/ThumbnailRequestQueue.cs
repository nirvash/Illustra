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
            bool success = false;
            try
            {
                LogHelper.LogWithTimestamp($"サムネイル処理実行中: {request.StartIndex}～{request.EndIndex}", LogHelper.Categories.ThumbnailQueue);

                // キャンセルされていないか確認
                request.CancellationToken.ThrowIfCancellationRequested();

                // サムネイル処理の実行
                for (int i = request.StartIndex; i <= request.EndIndex; i++)
                {
                    // 各インデックスごとにキャンセルチェック
                    request.CancellationToken.ThrowIfCancellationRequested();

                    // サムネイル生成処理を実行
                    await _thumbnailProcessor.CreateThumbnailAsync(i, request.CancellationToken);
                }

                success = true;
                LogHelper.LogWithTimestamp($"サムネイル処理完了: {request.StartIndex}～{request.EndIndex}", LogHelper.Categories.ThumbnailQueue);
            }
            catch (OperationCanceledException)
            {
                LogHelper.LogWithTimestamp($"サムネイル処理がキャンセルされました: {request.StartIndex}～{request.EndIndex}", LogHelper.Categories.ThumbnailQueue);
                success = false;
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"サムネイル処理中にエラーが発生しました: {ex.Message}", ex);
                success = false;
            }
            finally
            {
                try
                {
                    // 処理完了時にコールバックを呼び出す
                    request.CompletionCallback?.Invoke(request, success);
                }
                catch (Exception callbackEx)
                {
                    LogHelper.LogError($"コールバック実行中にエラーが発生しました: {callbackEx.Message}", callbackEx);
                }

                lock (_queueLock)
                {
                    // 現在のリクエストをクリアして処理中フラグをリセット
                    _currentRequest = null;
                    _isProcessing = false;

                    // 次のリクエストを処理
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
        /// キャンセルされたリクエストをキューから削除します
        /// </summary>
        public void CancelRequests(CancellationToken token)
        {
            lock (_queueLock)
            {
                // 高優先度リストからキャンセルされたリクエストを削除
                _highPriorityRequests.RemoveAll(request => request.CancellationToken.IsCancellationRequested);

                // 通常優先度リストからキャンセルされたリクエストを削除
                _normalPriorityRequests.RemoveAll(request => request.CancellationToken.IsCancellationRequested);

                LogHelper.LogWithTimestamp("キャンセルされたリクエストをキューから削除しました", LogHelper.Categories.ThumbnailQueue);
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
            // 実装例：重複する範囲のリクエストを統合する
            // 実際の実装はアプリケーションの要件に合わせて調整してください
        }
    }
}
