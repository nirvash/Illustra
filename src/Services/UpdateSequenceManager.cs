using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Illustra.Models;

namespace Illustra.Services
{
    /// <summary>
    /// 処理の種類を表す列挙型
    /// </summary>
    public enum OperationType
    {
        None,
        ThumbnailLoad,
        Filter,
        Sort
    }

    /// <summary>
    /// 処理の状態を表す列挙型
    /// </summary>
    public enum OperationState
    {
        NotStarted,
        Running,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// 処理の優先度を表す列挙型
    /// </summary>
    public enum OperationPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    /// <summary>
    /// 処理の進捗情報を表すクラス
    /// </summary>
    public class OperationProgress
    {
        public OperationType Type { get; set; }
        public int Percentage { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 処理状態変更イベントの引数
    /// </summary>
    public class OperationStateChangedEventArgs : EventArgs
    {
        public OperationType Type { get; }
        public OperationState State { get; }

        public OperationStateChangedEventArgs(OperationType type, OperationState state)
        {
            Type = type;
            State = state;
        }
    }

    /// <summary>
    /// 画像操作のパラメータを表すクラス
    /// </summary>
    public class ImageOperationParameters
    {
        public IEnumerable<FileNodeModel>? Images { get; set; }
        public int? RatingFilter { get; set; }
        public bool? SortByDate { get; set; }
        public bool? SortAscending { get; set; }
        public int ResumePoint { get; set; }
    }

    /// <summary>
    /// 処理を表すクラス
    /// </summary>
    /// <typeparam name="T">処理パラメータの型</typeparam>
    public class Operation<T>
    {
        public OperationType Type { get; }
        public T Parameters { get; }
        public Func<T, CancellationToken, IProgress<int>, Task> ExecuteAsync { get; }
        public OperationState State { get; set; } = OperationState.NotStarted;
        public int ResumePoint { get; set; }

        public Operation(OperationType type, T parameters, Func<T, CancellationToken, IProgress<int>, Task> executeAsync)
        {
            Type = type;
            Parameters = parameters;
            ExecuteAsync = executeAsync;
        }
    }

    /// <summary>
    /// 処理キューを表すクラス
    /// </summary>
    /// <typeparam name="T">処理パラメータの型</typeparam>
    public class OperationQueue<T>
    {
        private readonly PriorityQueue<Operation<T>, int> _queue = new();
        private CancellationTokenSource? _currentOperationCts;
        private Operation<T>? _currentOperation;

        /// <summary>
        /// 現在のキャンセルトークン
        /// </summary>
        public CancellationToken CurrentCancellationToken => _currentOperationCts?.Token ?? CancellationToken.None;

        /// <summary>
        /// 操作をキューに追加
        /// </summary>
        public void Enqueue(Operation<T> operation, int priority)
        {
            _queue.Enqueue(operation, -priority); // 優先度が高いほど先に処理されるよう負の値を使用
        }

        /// <summary>
        /// 現在の操作を中断
        /// </summary>
        public void Interrupt()
        {
            _currentOperationCts?.Cancel();
        }

        /// <summary>
        /// 高優先度操作による割り込み
        /// </summary>
        public void InterruptWithHighPriorityOperation(Operation<T> operation)
        {
            Interrupt();
            Enqueue(operation, (int)OperationPriority.Critical);
        }

        /// <summary>
        /// キューから次の操作を取得
        /// </summary>
        public bool TryDequeue(out Operation<T> operation, out int priority)
        {
            if (_queue.Count > 0)
            {
                _currentOperationCts = new CancellationTokenSource();
                var result = _queue.TryDequeue(out operation, out priority);
                if (result)
                {
                    _currentOperation = operation;
                    priority = -priority; // 元の優先度に戻す
                }
                return result;
            }

            operation = default!;
            priority = 0;
            return false;
        }
    }

    /// <summary>
    /// UpdateSequenceManagerのインターフェース
    /// </summary>
    public interface IUpdateSequenceManager
    {
        ImageCollectionModel ImageCollection { get; }
        OperationState CurrentState { get; }
        event EventHandler<OperationStateChangedEventArgs> StateChanged;
        void EnqueueThumbnailLoad(IEnumerable<FileNodeModel> images, bool isVisible);
        void InterruptWithFilterOperation(int ratingFilter);
        void InterruptWithSortOperation(bool sortByDate, bool sortAscending);
        Task WaitForOperationTypeCompletionAsync(OperationType type);
        Task<IList<FileNodeModel>> ExecuteFilterAsync(int rating, Func<Task> onCompleted = null);
        Task<IList<FileNodeModel>> ExecuteSortAsync(bool sortByDate, bool sortAscending, Func<Task> onCompleted = null);
    }

    /// <summary>
    /// 処理シーケンスの制御と優先度管理を担当するクラス
    /// </summary>
    public class UpdateSequenceManager : IUpdateSequenceManager
    {
        private readonly OperationQueue<ImageOperationParameters> _operationQueue = new();
        private readonly Progress<OperationProgress> _progress;
        private readonly ImageCollectionModel _imageCollection;
        private readonly SemaphoreSlim _processingLock = new(1, 1);
        private readonly Dictionary<OperationType, TaskCompletionSource<bool>> _operationCompletionSources = new();
        private readonly Timer _processingTimer;
        private bool _isProcessing;

        /// <summary>
        /// 画像コレクションモデル
        /// </summary>
        public ImageCollectionModel ImageCollection => _imageCollection;

        /// <summary>
        /// 現在の処理状態
        /// </summary>
        public OperationState CurrentState { get; private set; } = OperationState.NotStarted;

        /// <summary>
        /// 処理状態変更通知イベント
        /// </summary>
        public event EventHandler<OperationStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="imageCollection">画像コレクションモデル</param>
        public UpdateSequenceManager(ImageCollectionModel imageCollection)
        {
            _imageCollection = imageCollection;
            _progress = new Progress<OperationProgress>(OnProgressChanged);
            _processingTimer = new Timer(ProcessQueueCallback, null, 200, 200);
        }

        /// <summary>
        /// サムネイル読み込み操作をキューに追加
        /// </summary>
        public void EnqueueThumbnailLoad(IEnumerable<FileNodeModel> images, bool isVisible)
        {
            var priority = isVisible ? OperationPriority.High : OperationPriority.Normal;
            var operation = new Operation<ImageOperationParameters>(
                OperationType.ThumbnailLoad,
                new ImageOperationParameters { Images = images },
                ExecuteThumbnailLoadAsync
            );

            _operationQueue.Enqueue(operation, (int)priority);
        }

        /// <summary>
        /// フィルタ操作による割り込み
        /// </summary>
        public void InterruptWithFilterOperation(int ratingFilter)
        {
            // 現在の処理を中断
            _operationQueue.Interrupt();

            // フィルタ操作を高優先度で追加
            var filterOperation = new Operation<ImageOperationParameters>(
                OperationType.Filter,
                new ImageOperationParameters { RatingFilter = ratingFilter },
                ExecuteFilterOperationAsync
            );

            _operationQueue.InterruptWithHighPriorityOperation(filterOperation);

            // 完了待機用のTaskCompletionSourceを設定
            SetupOperationCompletionSource(OperationType.Filter);
        }

        /// <summary>
        /// ソート操作による割り込み
        /// </summary>
        public void InterruptWithSortOperation(bool sortByDate, bool sortAscending)
        {
            // 現在の処理を中断
            _operationQueue.Interrupt();

            // ソート操作を高優先度で追加
            var sortOperation = new Operation<ImageOperationParameters>(
                OperationType.Sort,
                new ImageOperationParameters { SortByDate = sortByDate, SortAscending = sortAscending },
                ExecuteSortOperationAsync
            );

            _operationQueue.InterruptWithHighPriorityOperation(sortOperation);

            // 完了待機用のTaskCompletionSourceを設定
            SetupOperationCompletionSource(OperationType.Sort);
        }

        /// <summary>
        /// 指定した種類の操作の完了を待機
        /// </summary>
        public async Task WaitForOperationTypeCompletionAsync(OperationType type)
        {
            if (!_operationCompletionSources.TryGetValue(type, out var tcs))
            {
                tcs = new TaskCompletionSource<bool>();
                _operationCompletionSources[type] = tcs;
            }

            await tcs.Task;
        }

        /// <summary>
        /// 操作完了ソースの設定
        /// </summary>
        private void SetupOperationCompletionSource(OperationType type)
        {
            if (_operationCompletionSources.TryGetValue(type, out var existingTcs))
            {
                // 既存のTaskCompletionSourceをリセット
                if (!existingTcs.Task.IsCompleted)
                {
                    existingTcs.TrySetCanceled();
                }
            }

            _operationCompletionSources[type] = new TaskCompletionSource<bool>();
        }

        /// <summary>
        /// 操作完了の通知
        /// </summary>
        private void CompleteOperation(OperationType type, bool success)
        {
            if (_operationCompletionSources.TryGetValue(type, out var tcs))
            {
                if (success)
                {
                    tcs.TrySetResult(true);
                }
                else
                {
                    tcs.TrySetException(new Exception($"Operation {type} failed"));
                }
            }
        }

        /// <summary>
        /// キュー処理のコールバック
        /// </summary>
        private void ProcessQueueCallback(object? state)
        {
            if (_isProcessing)
            {
                return;
            }

            _ = ProcessQueueAsync();
        }

        /// <summary>
        /// キューの処理
        /// </summary>
        private async Task ProcessQueueAsync()
        {
            if (!await _processingLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                _isProcessing = true;

                while (_operationQueue.TryDequeue(out var operation, out var priority))
                {
                    CurrentState = OperationState.Running;
                    NotifyStateChanged(operation.Type, OperationState.Running);

                    try
                    {
                        // 操作が中断されていた場合は再開ポイントから実行
                        if (operation.State == OperationState.Paused)
                        {
                            await ResumeOperationAsync(operation);
                        }
                        else
                        {
                            var progress = new Progress<int>(percentage =>
                            {
                                var progressInfo = new OperationProgress
                                {
                                    Type = operation.Type,
                                    Percentage = percentage,
                                    Message = $"Processing {operation.Type}..."
                                };
                                ((IProgress<OperationProgress>)_progress).Report(progressInfo);
                            });

                            await operation.ExecuteAsync(
                                operation.Parameters,
                                _operationQueue.CurrentCancellationToken,
                                progress
                            );
                        }

                        if (!_operationQueue.CurrentCancellationToken.IsCancellationRequested)
                        {
                            operation.State = OperationState.Completed;
                            CompleteOperation(operation.Type, true);
                            NotifyStateChanged(operation.Type, OperationState.Completed);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 操作が中断された場合は状態を保存
                        operation.State = OperationState.Paused;
                        NotifyStateChanged(operation.Type, OperationState.Paused);

                        // 必要に応じて再キューイング
                        if (ShouldRequeueOperation(operation))
                        {
                            _operationQueue.Enqueue(operation, CalculateRequeuePriority(priority));
                        }
                    }
                    catch (Exception ex)
                    {
                        operation.State = OperationState.Failed;
                        CompleteOperation(operation.Type, false);
                        NotifyStateChanged(operation.Type, OperationState.Failed);

                        // エラーハンドリング
                        HandleOperationError(operation, ex);
                    }
                }

                CurrentState = OperationState.NotStarted;
                NotifyStateChanged(OperationType.None, OperationState.NotStarted);
            }
            finally
            {
                _isProcessing = false;
                _processingLock.Release();
            }
        }

        /// <summary>
        /// 中断された操作の再開
        /// </summary>
        private async Task ResumeOperationAsync(Operation<ImageOperationParameters> operation)
        {
            // 再開ポイントから処理を実行
            switch (operation.Type)
            {
                case OperationType.ThumbnailLoad:
                    await ResumeThumbnailLoadAsync(operation);
                    break;
                case OperationType.Filter:
                    await ResumeFilterAsync(operation);
                    break;
                case OperationType.Sort:
                    await ResumeSortAsync(operation);
                    break;
            }
        }

        /// <summary>
        /// サムネイル読み込み操作の実行
        /// </summary>
        private async Task ExecuteThumbnailLoadAsync(ImageOperationParameters parameters, CancellationToken cancellationToken, IProgress<int> progress)
        {
            if (parameters.Images == null)
            {
                return;
            }

            // この段階では単純な実装
            // 実際の実装ではサムネイル生成処理を行う
            var images = parameters.Images.ToList();
            var total = images.Count;
            var processed = 0;

            foreach (var image in images)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // サムネイル生成処理（ダミー）
                await Task.Delay(10, cancellationToken);

                processed++;
                progress.Report((int)((float)processed / total * 100));
            }
        }

        /// <summary>
        /// フィルタ処理を実行する
        /// </summary>
        /// <param name="rating">レーティングフィルタ値</param>
        /// <param name="onCompleted">完了時のコールバック</param>
        /// <returns>フィルタリングされたアイテムのリスト</returns>
        public async Task<IList<FileNodeModel>> ExecuteFilterAsync(int rating, Func<Task> onCompleted = null)
        {
            // 現在の処理を中断
            _operationQueue.Interrupt();

            // 状態変更通知
            NotifyStateChanged(OperationType.Filter, OperationState.Running);

            try
            {
                // フィルタ処理を実行
                var result = await _imageCollection.FilterByRatingAsync(rating);

                // 完了通知
                NotifyStateChanged(OperationType.Filter, OperationState.Completed);

                // コールバックがあれば実行
                if (onCompleted != null)
                {
                    await onCompleted();
                }

                return result;
            }
            catch (Exception ex)
            {
                // エラー通知
                NotifyStateChanged(OperationType.Filter, OperationState.Failed);
                System.Diagnostics.Debug.WriteLine($"Filter error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// ソート処理を実行する
        /// </summary>
        /// <param name="sortByDate">日付順でソートするかどうか</param>
        /// <param name="sortAscending">昇順でソートするかどうか</param>
        /// <param name="onCompleted">完了時のコールバック</param>
        /// <returns>ソートされたアイテムのリスト</returns>
        public async Task<IList<FileNodeModel>> ExecuteSortAsync(bool sortByDate, bool sortAscending, Func<Task> onCompleted = null)
        {
            // 現在の処理を中断
            _operationQueue.Interrupt();

            // 状態変更通知
            NotifyStateChanged(OperationType.Sort, OperationState.Running);

            try
            {
                // ソート処理を実行
                var result = await _imageCollection.SortItemsAsync(sortByDate, sortAscending);

                // 完了通知
                NotifyStateChanged(OperationType.Sort, OperationState.Completed);

                // コールバックがあれば実行
                if (onCompleted != null)
                {
                    await onCompleted();
                }

                return result;
            }
            catch (Exception ex)
            {
                // エラー通知
                NotifyStateChanged(OperationType.Sort, OperationState.Failed);
                System.Diagnostics.Debug.WriteLine($"Sort error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// サムネイル読み込み操作の再開
        /// </summary>
        private async Task ResumeThumbnailLoadAsync(Operation<ImageOperationParameters> operation)
        {
            // 実装は後で追加
            await Task.CompletedTask;
        }

        /// <summary>
        /// フィルタ操作の再開
        /// </summary>
        private async Task ResumeFilterAsync(Operation<ImageOperationParameters> operation)
        {
            // 実装は後で追加
            await Task.CompletedTask;
        }

        /// <summary>
        /// ソート操作の再開
        /// </summary>
        private async Task ResumeSortAsync(Operation<ImageOperationParameters> operation)
        {
            // 実装は後で追加
            await Task.CompletedTask;
        }

        /// <summary>
        /// 操作を再キューイングするかどうかを判断
        /// </summary>
        private bool ShouldRequeueOperation(Operation<ImageOperationParameters> operation)
        {
            // 基本的な実装
            // サムネイル読み込みは表示範囲内のみ再キューイング
            return operation.Type == OperationType.ThumbnailLoad;
        }

        /// <summary>
        /// 再キューイング時の優先度を計算
        /// </summary>
        private int CalculateRequeuePriority(int originalPriority)
        {
            // 基本的な実装
            // 優先度を1段階下げる
            return Math.Max(0, originalPriority - 1);
        }

        /// <summary>
        /// 操作エラーの処理
        /// </summary>
        private void HandleOperationError(Operation<ImageOperationParameters> operation, Exception ex)
        {
            // エラーログ記録などの処理
            System.Diagnostics.Debug.WriteLine($"Operation error: {operation.Type}, {ex.Message}");
        }

        /// <summary>
        /// 進捗変更の処理
        /// </summary>
        private void OnProgressChanged(OperationProgress progress)
        {
            // 進捗情報の処理
            System.Diagnostics.Debug.WriteLine($"Progress: {progress.Type}, {progress.Percentage}%, {progress.Message}");
        }

        /// <summary>
        /// 状態変更の通知
        /// </summary>
        private void NotifyStateChanged(OperationType type, OperationState state)
        {
            StateChanged?.Invoke(this, new OperationStateChangedEventArgs(type, state));
        }

        /// <summary>
        /// フィルタ操作の実行（内部用）
        /// </summary>
        private async Task ExecuteFilterOperationAsync(ImageOperationParameters parameters, CancellationToken cancellationToken, IProgress<int> progress)
        {
            if (parameters.RatingFilter == null)
            {
                return;
            }

            // フィルタ処理
            progress.Report(0);
            await _imageCollection.FilterByRatingAsync(parameters.RatingFilter.Value, cancellationToken);
            progress.Report(100);
        }

        /// <summary>
        /// ソート操作の実行（内部用）
        /// </summary>
        private async Task ExecuteSortOperationAsync(ImageOperationParameters parameters, CancellationToken cancellationToken, IProgress<int> progress)
        {
            if (parameters.SortByDate == null || parameters.SortAscending == null)
            {
                return;
            }

            // ソート処理
            progress.Report(0);
            await _imageCollection.SortItemsAsync(parameters.SortByDate.Value, parameters.SortAscending.Value, cancellationToken);
            progress.Report(100);
        }
    }
}