using System.Threading;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider;

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
    public class DatabaseAccess
    {
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private readonly IDataProvider _dataProvider;
        private readonly string _connectionString;
        private const int DefaultRetryCount = 5;
        private const int DefaultRetryDelayMilliseconds = 200;
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        public DatabaseAccess(IDataProvider dataProvider, string connectionString)
        {
            _dataProvider = dataProvider;
            _connectionString = connectionString;
        }

        public async Task<T> ReadAsync<T>(Func<DataConnection, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = new CancellationTokenSource(DefaultTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            _lock.EnterReadLock();
            try
            {
                using var db = new DataConnection(_dataProvider, _connectionString);
                linkedCts.Token.ThrowIfCancellationRequested();

                var task = operation(db);
                await Task.WhenAny(task, Task.Delay(-1, linkedCts.Token));

                linkedCts.Token.ThrowIfCancellationRequested();
                return await task;
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                throw new TimeoutException($"Database read operation timed out after {DefaultTimeout.TotalSeconds} seconds");
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public async Task WriteAsync(Func<DataConnection, Task> operation)
        {
            _lock.EnterWriteLock();
            try
            {
                await ExecuteWithRetryAsync(async () =>
                {
                    using var db = new DataConnection(_dataProvider, _connectionString);
                    await operation(db);
                });
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public async Task<T> WriteWithResultAsync<T>(Func<DataConnection, Task<T>> operation)
        {
            _lock.EnterWriteLock();
            try
            {
                return await ExecuteWithRetryAsync(async () =>
                {
                    using var db = new DataConnection(_dataProvider, _connectionString);
                    return await operation(db);
                });
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private async Task ExecuteWithRetryAsync(Func<Task> operation)
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
                        throw;
                    }
                    int delay = DefaultRetryDelayMilliseconds * (int)Math.Pow(2, retryCount - 1); // Exponential backoff
                    Debug.WriteLine($"Database is locked, retrying in {delay} ms");
                    await Task.Delay(delay);
                }
            }
        }

        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation)
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
                        throw;
                    }
                    int delay = DefaultRetryDelayMilliseconds * (int)Math.Pow(2, retryCount - 1); // Exponential backoff
                    Debug.WriteLine($"Database is locked, retrying in {delay} ms");
                    await Task.Delay(delay);
                }
            }
        }

        public async Task WriteWithTransactionAsync(Func<DataConnection, Task> operation)
        {
            _lock.EnterWriteLock();
            try
            {
                await ExecuteWithRetryAsync(async () =>
                {
                    using var db = new DataConnection(_dataProvider, _connectionString);
                    await db.BeginTransactionAsync();
                    try
                    {
                        await operation(db);
                        await db.CommitTransactionAsync();
                    }
                    catch
                    {
                        await db.RollbackTransactionAsync();
                        throw;
                    }
                });
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public async Task<T> WriteWithTransactionAsync<T>(Func<DataConnection, Task<T>> operation)
        {
            _lock.EnterWriteLock();
            try
            {
                return await ExecuteWithRetryAsync(async () =>
                {
                    using var db = new DataConnection(_dataProvider, _connectionString);
                    await db.BeginTransactionAsync();
                    try
                    {
                        var result = await operation(db);
                        await db.CommitTransactionAsync();
                        return result;
                    }
                    catch
                    {
                        await db.RollbackTransactionAsync();
                        throw;
                    }
                });
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }
}
