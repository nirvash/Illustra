using System.Threading;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider;
using LinqToDB.Interceptors;
using System.Collections.Concurrent;
using System.Text;
using System.Data.Common;

namespace Illustra.Helpers
{
    /// <summary>
    /// データベースアクセスを制御するクラス。
    /// 読み書きの同時実行制御とエラーハンドリングを提供します。
    ///
    /// 注意点：
    /// 1. デフォルトタイムアウトは30秒に設定していますが、実際の運用状況に応じて調整が必要かもしれません。
    ///    大量データの処理や複雑なクエリの場合は、より長いタイムアウト時間が必要になる可能性があります。
    ///
    /// 2. 現在はSQLiteErrorCode == 5 (database is locked)のみを特別扱いしていますが、
    ///    以下のようなエラーも考慮が必要かもしれません：
    ///    - SQLITE_BUSY (5): データベースファイルがロックされている
    ///    - SQLITE_IOERR (10): ディスクI/Oエラー
    ///    - SQLITE_CORRUPT (11): データベースが破損している
    ///    - SQLITE_FULL (13): データベースが一杯
    ///    これらのエラーに対する適切なハンドリングは、実際の運用状況を見て追加を検討します。
    /// </summary>
    public class DatabaseAccess : IDisposable
    {
        // 書き込み優先のReaderWriterLockSlimを使用
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private readonly IDataProvider _dataProvider;
        private readonly string _connectionString;
        private const int DefaultRetryCount = 5;
        private const int DefaultRetryDelayMilliseconds = 200;
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        // 同時実行数を制限するためのセマフォ
        private readonly SemaphoreSlim _readSemaphore = new SemaphoreSlim(10, 10); // 最大10の同時読み取り
        private readonly SemaphoreSlim _writeSemaphore = new SemaphoreSlim(1, 1);  // 書き込みは1つずつ

        // 現在実行中の操作数を追跡
        private int _activeReadOperations = 0;
        private int _activeWriteOperations = 0;
        private int _pendingWriteOperations = 0;

        // 操作IDを生成するためのカウンター
        private int _operationIdCounter = 0;

        // デバッグログの設定
        public bool EnableDebugLogging { get; set; } = false;
        public bool EnableVerboseLogging { get; set; } = false;

        // 最近のログを保持するリングバッファ（最大1000エントリ）
        private readonly ConcurrentQueue<string> _logBuffer = new ConcurrentQueue<string>();
        private const int MaxLogBufferSize = 1000;

        public DatabaseAccess(IDataProvider dataProvider, string connectionString)
        {
            _dataProvider = dataProvider;
            _connectionString = connectionString;
        }

        // 操作IDを生成
        private int GenerateOperationId()
        {
            return Interlocked.Increment(ref _operationIdCounter);
        }

        // デバッグログを出力
        private void LogDebug(string message)
        {
            if (!EnableDebugLogging) return;

            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [DB] {message}";
            Debug.WriteLine(logEntry);

            // ログバッファにも追加
            _logBuffer.Enqueue(logEntry);

            // バッファサイズを制限
            while (_logBuffer.Count > MaxLogBufferSize && _logBuffer.TryDequeue(out _)) { }
        }

        // 詳細ログを出力
        private void LogVerbose(string message)
        {
            if (!EnableVerboseLogging) return;
            LogDebug(message);
        }

        // 最近のログを取得
        public string GetRecentLogs()
        {
            var sb = new StringBuilder();
            foreach (var log in _logBuffer)
            {
                sb.AppendLine(log);
            }
            return sb.ToString();
        }

        // 現在のデータベース操作の状態を取得するメソッド（デバッグ用）
        public (int ActiveReads, int ActiveWrites, int PendingWrites) GetOperationStatus()
        {
            return (_activeReadOperations, _activeWriteOperations, _pendingWriteOperations);
        }

        public async Task<T> ReadAsync<T>(Func<DataConnection, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            int operationId = GenerateOperationId();
            string operationName = GetOperationName(operation);
            LogDebug($"[READ-{operationId}] 開始: {operationName}");

            using var timeoutCts = new CancellationTokenSource(DefaultTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var token = linkedCts.Token;

            // 読み取りセマフォを取得
            LogVerbose($"[READ-{operationId}] 読み取りセマフォ待機中 (現在のアクティブ読み取り: {_activeReadOperations})");
            await _readSemaphore.WaitAsync(token);
            LogVerbose($"[READ-{operationId}] 読み取りセマフォ取得");

            try
            {
                // 書き込み操作が待機中の場合、短時間待機して書き込みを優先
                if (_pendingWriteOperations > 0)
                {
                    LogVerbose($"[READ-{operationId}] 書き込み操作待機中 ({_pendingWriteOperations}件)、短時間待機");
                    try
                    {
                        await Task.Delay(50, token); // 書き込み優先のための短い待機
                    }
                    catch (OperationCanceledException)
                    {
                        LogDebug($"[READ-{operationId}] キャンセルされました (書き込み優先待機中): {operationName}");
                        throw;
                    }
                }

                // アクティブな読み取り操作をインクリメント
                Interlocked.Increment(ref _activeReadOperations);
                LogVerbose($"[READ-{operationId}] アクティブな読み取り操作: {_activeReadOperations}");

                // 読み取りロックを取得
                LogVerbose($"[READ-{operationId}] 読み取りロック取得中");
                _lock.EnterReadLock();
                LogVerbose($"[READ-{operationId}] 読み取りロック取得完了");

                try
                {
                    // リトライロジックを追加
                    return await ExecuteWithRetryAsync(async () =>
                    {
                        LogVerbose($"[READ-{operationId}] データベース接続作成");
                        using var db = new DataConnection(_dataProvider, _connectionString);
                        token.ThrowIfCancellationRequested();

                        try
                        {
                            // 読み取り操作用のトランザクションを開始（読み取り一貫性のため）
                            LogVerbose($"[READ-{operationId}] トランザクション開始");
                            await db.BeginTransactionAsync();

                            try
                            {
                                // SQLクエリをキャプチャするためのインターセプターを設定
                                var commandInterceptor = new CommandInterceptor();
                                db.AddInterceptor(commandInterceptor);

                                LogVerbose($"[READ-{operationId}] クエリ実行");
                                var result = await operation(db);

                                // 実行されたSQLクエリをログに記録
                                if (commandInterceptor.LastCommand != null)
                                {
                                    LogDebug($"[READ-{operationId}] 実行SQL: {commandInterceptor.LastCommand.CommandText}");
                                    if (EnableVerboseLogging && commandInterceptor.LastCommand.Parameters.Count > 0)
                                    {
                                        LogVerbose($"[READ-{operationId}] パラメータ: {FormatParameters(commandInterceptor.LastCommand.Parameters)}");
                                    }
                                }

                                LogVerbose($"[READ-{operationId}] クエリ完了");

                                LogVerbose($"[READ-{operationId}] トランザクションコミット");
                                await db.CommitTransactionAsync();
                                return result;
                            }
                            catch (Exception ex)
                            {
                                LogDebug($"[READ-{operationId}] クエリ実行中にエラー: {ex.Message}, 操作: {operationName}");
                                await db.RollbackTransactionAsync();
                                throw;
                            }
                        }
                        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                        {
                            LogDebug($"[READ-{operationId}] タイムアウト ({DefaultTimeout.TotalSeconds}秒): {operationName}");
                            throw new TimeoutException($"Database read operation timed out after {DefaultTimeout.TotalSeconds} seconds");
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"[READ-{operationId}] エラー: {ex.Message}, 操作: {operationName}");
                            throw;
                        }
                    }, token, operationId, "READ", operationName);
                }
                finally
                {
                    LogVerbose($"[READ-{operationId}] 読み取りロック解放");
                    _lock.ExitReadLock();
                    Interlocked.Decrement(ref _activeReadOperations);
                    LogVerbose($"[READ-{operationId}] アクティブな読み取り操作: {_activeReadOperations}");
                }
            }
            finally
            {
                LogVerbose($"[READ-{operationId}] 読み取りセマフォ解放");
                _readSemaphore.Release();
                LogDebug($"[READ-{operationId}] 終了: {operationName}");
            }
        }

        public async Task WriteAsync(Func<DataConnection, Task> operation, CancellationToken cancellationToken = default)
        {
            int operationId = GenerateOperationId();
            string operationName = GetOperationName(operation);
            LogDebug($"[WRITE-{operationId}] 開始: {operationName}");

            // 書き込みセマフォを取得
            LogVerbose($"[WRITE-{operationId}] 書き込みセマフォ待機中 (現在のアクティブ書き込み: {_activeWriteOperations})");
            await _writeSemaphore.WaitAsync(cancellationToken);
            LogVerbose($"[WRITE-{operationId}] 書き込みセマフォ取得");

            // 待機中の書き込み操作をインクリメント
            Interlocked.Increment(ref _pendingWriteOperations);
            LogVerbose($"[WRITE-{operationId}] 待機中の書き込み操作: {_pendingWriteOperations}");

            try
            {
                // アクティブな書き込み操作をインクリメント
                Interlocked.Increment(ref _activeWriteOperations);
                LogVerbose($"[WRITE-{operationId}] アクティブな書き込み操作: {_activeWriteOperations}");

                // 書き込みロックを取得
                LogVerbose($"[WRITE-{operationId}] 書き込みロック取得中");
                _lock.EnterWriteLock();
                LogVerbose($"[WRITE-{operationId}] 書き込みロック取得完了");

                try
                {
                    await ExecuteWithRetryAsync(async () =>
                    {
                        LogVerbose($"[WRITE-{operationId}] データベース接続作成");
                        using var db = new DataConnection(_dataProvider, _connectionString);

                        try
                        {
                            // SQLクエリをキャプチャするためのインターセプターを設定
                            var commandInterceptor = new CommandInterceptor();
                            db.AddInterceptor(commandInterceptor);

                            LogVerbose($"[WRITE-{operationId}] クエリ実行");
                            await operation(db);

                            // 実行されたSQLクエリをログに記録
                            if (commandInterceptor.LastCommand != null)
                            {
                                LogDebug($"[WRITE-{operationId}] 実行SQL: {commandInterceptor.LastCommand.CommandText}");
                                if (EnableVerboseLogging && commandInterceptor.LastCommand.Parameters.Count > 0)
                                {
                                    LogVerbose($"[WRITE-{operationId}] パラメータ: {FormatParameters(commandInterceptor.LastCommand.Parameters)}");
                                }
                            }

                            LogVerbose($"[WRITE-{operationId}] クエリ完了");
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"[WRITE-{operationId}] クエリ実行中にエラー: {ex.Message}, 操作: {operationName}");
                            throw;
                        }
                    }, cancellationToken, operationId, "WRITE", operationName);
                }
                finally
                {
                    LogVerbose($"[WRITE-{operationId}] 書き込みロック解放");
                    _lock.ExitWriteLock();
                    Interlocked.Decrement(ref _activeWriteOperations);
                    LogVerbose($"[WRITE-{operationId}] アクティブな書き込み操作: {_activeWriteOperations}");
                }
            }
            finally
            {
                Interlocked.Decrement(ref _pendingWriteOperations);
                LogVerbose($"[WRITE-{operationId}] 待機中の書き込み操作: {_pendingWriteOperations}");

                LogVerbose($"[WRITE-{operationId}] 書き込みセマフォ解放");
                _writeSemaphore.Release();
                LogDebug($"[WRITE-{operationId}] 終了: {operationName}");
            }
        }

        public async Task<T> WriteWithResultAsync<T>(Func<DataConnection, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            int operationId = GenerateOperationId();
            string operationName = GetOperationName(operation);
            LogDebug($"[WRITE_RESULT-{operationId}] 開始: {operationName}");

            // 書き込みセマフォを取得
            LogVerbose($"[WRITE_RESULT-{operationId}] 書き込みセマフォ待機中");
            await _writeSemaphore.WaitAsync(cancellationToken);
            LogVerbose($"[WRITE_RESULT-{operationId}] 書き込みセマフォ取得");

            // 待機中の書き込み操作をインクリメント
            Interlocked.Increment(ref _pendingWriteOperations);
            LogVerbose($"[WRITE_RESULT-{operationId}] 待機中の書き込み操作: {_pendingWriteOperations}");

            try
            {
                // アクティブな書き込み操作をインクリメント
                Interlocked.Increment(ref _activeWriteOperations);
                LogVerbose($"[WRITE_RESULT-{operationId}] アクティブな書き込み操作: {_activeWriteOperations}");

                // 書き込みロックを取得
                LogVerbose($"[WRITE_RESULT-{operationId}] 書き込みロック取得中");
                _lock.EnterWriteLock();
                LogVerbose($"[WRITE_RESULT-{operationId}] 書き込みロック取得完了");

                try
                {
                    return await ExecuteWithRetryAsync(async () =>
                    {
                        LogVerbose($"[WRITE_RESULT-{operationId}] データベース接続作成");
                        using var db = new DataConnection(_dataProvider, _connectionString);

                        try
                        {
                            // SQLクエリをキャプチャするためのインターセプターを設定
                            var commandInterceptor = new CommandInterceptor();
                            db.AddInterceptor(commandInterceptor);

                            LogVerbose($"[WRITE_RESULT-{operationId}] クエリ実行");
                            var result = await operation(db);

                            // 実行されたSQLクエリをログに記録
                            if (commandInterceptor.LastCommand != null)
                            {
                                LogDebug($"[WRITE_RESULT-{operationId}] 実行SQL: {commandInterceptor.LastCommand.CommandText}");
                                if (EnableVerboseLogging && commandInterceptor.LastCommand.Parameters.Count > 0)
                                {
                                    LogVerbose($"[WRITE_RESULT-{operationId}] パラメータ: {FormatParameters(commandInterceptor.LastCommand.Parameters)}");
                                }
                            }

                            LogVerbose($"[WRITE_RESULT-{operationId}] クエリ完了");
                            return result;
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"[WRITE_RESULT-{operationId}] クエリ実行中にエラー: {ex.Message}, 操作: {operationName}");
                            throw;
                        }
                    }, cancellationToken, operationId, "WRITE_RESULT", operationName);
                }
                finally
                {
                    LogVerbose($"[WRITE_RESULT-{operationId}] 書き込みロック解放");
                    _lock.ExitWriteLock();
                    Interlocked.Decrement(ref _activeWriteOperations);
                    LogVerbose($"[WRITE_RESULT-{operationId}] アクティブな書き込み操作: {_activeWriteOperations}");
                }
            }
            finally
            {
                Interlocked.Decrement(ref _pendingWriteOperations);
                LogVerbose($"[WRITE_RESULT-{operationId}] 待機中の書き込み操作: {_pendingWriteOperations}");

                LogVerbose($"[WRITE_RESULT-{operationId}] 書き込みセマフォ解放");
                _writeSemaphore.Release();
                LogDebug($"[WRITE_RESULT-{operationId}] 終了: {operationName}");
            }
        }

        public async Task WriteWithTransactionAsync(Func<DataConnection, Task> operation, CancellationToken cancellationToken = default)
        {
            int operationId = GenerateOperationId();
            string operationName = GetOperationName(operation);
            LogDebug($"[WRITE_TX-{operationId}] 開始: {operationName}");

            // 書き込みセマフォを取得
            LogVerbose($"[WRITE_TX-{operationId}] 書き込みセマフォ待機中");
            await _writeSemaphore.WaitAsync(cancellationToken);
            LogVerbose($"[WRITE_TX-{operationId}] 書き込みセマフォ取得");

            // 待機中の書き込み操作をインクリメント
            Interlocked.Increment(ref _pendingWriteOperations);
            LogVerbose($"[WRITE_TX-{operationId}] 待機中の書き込み操作: {_pendingWriteOperations}");

            try
            {
                // アクティブな書き込み操作をインクリメント
                Interlocked.Increment(ref _activeWriteOperations);
                LogVerbose($"[WRITE_TX-{operationId}] アクティブな書き込み操作: {_activeWriteOperations}");

                // 書き込みロックを取得
                LogVerbose($"[WRITE_TX-{operationId}] 書き込みロック取得中");
                _lock.EnterWriteLock();
                LogVerbose($"[WRITE_TX-{operationId}] 書き込みロック取得完了");

                try
                {
                    await ExecuteWithRetryAsync(async () =>
                    {
                        LogVerbose($"[WRITE_TX-{operationId}] データベース接続作成");
                        using var db = new DataConnection(_dataProvider, _connectionString);

                        // SQLクエリをキャプチャするためのインターセプターを設定
                        var commandInterceptor = new CommandInterceptor();
                        db.AddInterceptor(commandInterceptor);

                        LogVerbose($"[WRITE_TX-{operationId}] トランザクション開始");
                        await db.BeginTransactionAsync();
                        try
                        {
                            LogVerbose($"[WRITE_TX-{operationId}] クエリ実行");
                            await operation(db);

                            // 実行されたSQLクエリをログに記録
                            if (commandInterceptor.LastCommand != null)
                            {
                                LogDebug($"[WRITE_TX-{operationId}] 実行SQL: {commandInterceptor.LastCommand.CommandText}");
                                if (EnableVerboseLogging && commandInterceptor.LastCommand.Parameters.Count > 0)
                                {
                                    LogVerbose($"[WRITE_TX-{operationId}] パラメータ: {FormatParameters(commandInterceptor.LastCommand.Parameters)}");
                                }
                            }

                            LogVerbose($"[WRITE_TX-{operationId}] クエリ完了");

                            LogVerbose($"[WRITE_TX-{operationId}] トランザクションコミット");
                            await db.CommitTransactionAsync();
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"[WRITE_TX-{operationId}] エラー発生、ロールバック: {ex.Message}, 操作: {operationName}");
                            await db.RollbackTransactionAsync();
                            throw;
                        }
                    }, cancellationToken, operationId, "WRITE_TX", operationName);
                }
                finally
                {
                    LogVerbose($"[WRITE_TX-{operationId}] 書き込みロック解放");
                    _lock.ExitWriteLock();
                    Interlocked.Decrement(ref _activeWriteOperations);
                    LogVerbose($"[WRITE_TX-{operationId}] アクティブな書き込み操作: {_activeWriteOperations}");
                }
            }
            finally
            {
                Interlocked.Decrement(ref _pendingWriteOperations);
                LogVerbose($"[WRITE_TX-{operationId}] 待機中の書き込み操作: {_pendingWriteOperations}");

                LogVerbose($"[WRITE_TX-{operationId}] 書き込みセマフォ解放");
                _writeSemaphore.Release();
                LogDebug($"[WRITE_TX-{operationId}] 終了: {operationName}");
            }
        }

        public async Task<T> WriteWithTransactionAsync<T>(Func<DataConnection, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            int operationId = GenerateOperationId();
            string operationName = GetOperationName(operation);
            LogDebug($"[WRITE_TX_RESULT-{operationId}] 開始: {operationName}");

            // 書き込みセマフォを取得
            LogVerbose($"[WRITE_TX_RESULT-{operationId}] 書き込みセマフォ待機中");
            await _writeSemaphore.WaitAsync(cancellationToken);
            LogVerbose($"[WRITE_TX_RESULT-{operationId}] 書き込みセマフォ取得");

            // 待機中の書き込み操作をインクリメント
            Interlocked.Increment(ref _pendingWriteOperations);
            LogVerbose($"[WRITE_TX_RESULT-{operationId}] 待機中の書き込み操作: {_pendingWriteOperations}");

            try
            {
                // アクティブな書き込み操作をインクリメント
                Interlocked.Increment(ref _activeWriteOperations);
                LogVerbose($"[WRITE_TX_RESULT-{operationId}] アクティブな書き込み操作: {_activeWriteOperations}");

                // 書き込みロックを取得
                LogVerbose($"[WRITE_TX_RESULT-{operationId}] 書き込みロック取得中");
                _lock.EnterWriteLock();
                LogVerbose($"[WRITE_TX_RESULT-{operationId}] 書き込みロック取得完了");

                try
                {
                    return await ExecuteWithRetryAsync(async () =>
                    {
                        LogVerbose($"[WRITE_TX_RESULT-{operationId}] データベース接続作成");
                        using var db = new DataConnection(_dataProvider, _connectionString);

                        // SQLクエリをキャプチャするためのインターセプターを設定
                        var commandInterceptor = new CommandInterceptor();
                        db.AddInterceptor(commandInterceptor);

                        LogVerbose($"[WRITE_TX_RESULT-{operationId}] トランザクション開始");
                        await db.BeginTransactionAsync();
                        try
                        {
                            LogVerbose($"[WRITE_TX_RESULT-{operationId}] クエリ実行");
                            var result = await operation(db);

                            // 実行されたSQLクエリをログに記録
                            if (commandInterceptor.LastCommand != null)
                            {
                                LogDebug($"[WRITE_TX_RESULT-{operationId}] 実行SQL: {commandInterceptor.LastCommand.CommandText}");
                                if (EnableVerboseLogging && commandInterceptor.LastCommand.Parameters.Count > 0)
                                {
                                    LogVerbose($"[WRITE_TX_RESULT-{operationId}] パラメータ: {FormatParameters(commandInterceptor.LastCommand.Parameters)}");
                                }
                            }

                            LogVerbose($"[WRITE_TX_RESULT-{operationId}] クエリ完了");

                            LogVerbose($"[WRITE_TX_RESULT-{operationId}] トランザクションコミット");
                            await db.CommitTransactionAsync();
                            return result;
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"[WRITE_TX_RESULT-{operationId}] エラー発生、ロールバック: {ex.Message}, 操作: {operationName}");
                            await db.RollbackTransactionAsync();
                            throw;
                        }
                    }, cancellationToken, operationId, "WRITE_TX_RESULT", operationName);
                }
                finally
                {
                    LogVerbose($"[WRITE_TX_RESULT-{operationId}] 書き込みロック解放");
                    _lock.ExitWriteLock();
                    Interlocked.Decrement(ref _activeWriteOperations);
                    LogVerbose($"[WRITE_TX_RESULT-{operationId}] アクティブな書き込み操作: {_activeWriteOperations}");
                }
            }
            finally
            {
                Interlocked.Decrement(ref _pendingWriteOperations);
                LogVerbose($"[WRITE_TX_RESULT-{operationId}] 待機中の書き込み操作: {_pendingWriteOperations}");

                LogVerbose($"[WRITE_TX_RESULT-{operationId}] 書き込みセマフォ解放");
                _writeSemaphore.Release();
                LogDebug($"[WRITE_TX_RESULT-{operationId}] 終了: {operationName}");
            }
        }

        // 操作の名前を取得するヘルパーメソッド
        private string GetOperationName(Delegate operation)
        {
            try
            {
                var method = operation.Method;
                if (method.DeclaringType != null)
                {
                    // ラムダ式の場合は呼び出し元のメソッド名を取得
                    if (method.Name.Contains("<") && method.Name.Contains(">"))
                    {
                        var stackTrace = new StackTrace(2, false);
                        for (int i = 0; i < stackTrace.FrameCount; i++)
                        {
                            var frame = stackTrace.GetFrame(i);
                            if (frame != null && frame.GetMethod()?.DeclaringType != GetType())
                            {
                                var callingMethod = frame.GetMethod();
                                if (callingMethod != null && callingMethod.DeclaringType != null)
                                {
                                    return $"{callingMethod.DeclaringType.Name}.{callingMethod.Name}";
                                }
                            }
                        }
                        return "Lambda";
                    }
                    return $"{method.DeclaringType.Name}.{method.Name}";
                }
                return method.Name;
            }
            catch
            {
                return "Unknown";
            }
        }

        // SQLパラメータをフォーマットするヘルパーメソッド
        private string FormatParameters(DbParameterCollection parameters)
        {
            var sb = new StringBuilder();
            foreach (DbParameter param in parameters)
            {
                sb.Append($"{param.ParameterName}={param.Value}, ");
            }
            return sb.ToString().TrimEnd(',', ' ');
        }

        // SQLコマンドをキャプチャするインターセプター
        private class CommandInterceptor : IInterceptor
        {
            public DbCommand? LastCommand { get; private set; }

            public void OnClosing(DataConnection dataConnection) { }
            public void OnClosed(DataConnection dataConnection) { }

            public void OnCommandExecuted(DataConnection dataConnection, DbCommand command)
            {
                LastCommand = command;
            }

            public void OnCommandExecuting(DataConnection dataConnection, DbCommand command) { }
            public void OnConnectionOpened(DataConnection dataConnection, ConnectionEventData eventData) { }
            public void OnConnectionOpening(DataConnection dataConnection, ConnectionEventData eventData) { }
            public void OnConnectionClosing(DataConnection dataConnection, ConnectionEventData eventData) { }
            public void OnConnectionClosed(DataConnection dataConnection, ConnectionEventData eventData) { }
        }

        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default, int operationId = 0, string operationType = "", string operationName = "")
        {
            int retryCount = 0;
            while (true)
            {
                try
                {
                    return await operation();
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5) // SQLite Error 5: 'database is locked'
                {
                    retryCount++;
                    if (retryCount > DefaultRetryCount)
                    {
                        LogDebug($"[{operationType}-{operationId}] データベースロックエラー、最大リトライ回数({DefaultRetryCount})を超えました: {operationName}");
                        throw;
                    }

                    int delay = DefaultRetryDelayMilliseconds * (int)Math.Pow(2, retryCount - 1); // Exponential backoff
                    LogDebug($"[{operationType}-{operationId}] データベースがロックされています、{delay} ms後に再試行します (試行回数: {retryCount}/{DefaultRetryCount}): {operationName}");

                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        LogDebug($"[{operationType}-{operationId}] キャンセルされました (リトライ待機中): {operationName}");
                        throw;
                    }
                }
                catch (OperationCanceledException)
                {
                    LogDebug($"[{operationType}-{operationId}] キャンセルされました: {operationName}");
                    throw;
                }
            }
        }

        private async Task ExecuteWithRetryAsync(Func<Task> operation, CancellationToken cancellationToken = default, int operationId = 0, string operationType = "", string operationName = "")
        {
            int retryCount = 0;
            while (true)
            {
                try
                {
                    await operation();
                    break;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5) // SQLite Error 5: 'database is locked'
                {
                    retryCount++;
                    if (retryCount > DefaultRetryCount)
                    {
                        LogDebug($"[{operationType}-{operationId}] データベースロックエラー、最大リトライ回数({DefaultRetryCount})を超えました: {operationName}");
                        throw;
                    }

                    int delay = DefaultRetryDelayMilliseconds * (int)Math.Pow(2, retryCount - 1); // Exponential backoff
                    LogDebug($"[{operationType}-{operationId}] データベースがロックされています、{delay} ms後に再試行します (試行回数: {retryCount}/{DefaultRetryCount}): {operationName}");

                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        LogDebug($"[{operationType}-{operationId}] キャンセルされました (リトライ待機中): {operationName}");
                        throw;
                    }
                }
                catch (OperationCanceledException)
                {
                    LogDebug($"[{operationType}-{operationId}] キャンセルされました: {operationName}");
                    throw;
                }
            }
        }

        // リソースの解放
        public void Dispose()
        {
            LogDebug("DatabaseAccess リソース解放");
            _lock?.Dispose();
            _readSemaphore?.Dispose();
            _writeSemaphore?.Dispose();
        }
    }
}
