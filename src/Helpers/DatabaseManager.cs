using System.IO;
using Microsoft.Data.Sqlite;
using Illustra.Models;
using System.Diagnostics;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Configuration;
using LinqToDB.DataProvider.SQLite;
using LinqToDB.DataProvider;

namespace Illustra.Helpers
{
    public class DatabaseManager
    {
        private readonly string _connectionString;
        private readonly IDataProvider _dataProvider;
        private readonly string _dbPath;
        private readonly DatabaseAccess _dbAccess;
        private const int CurrentSchemaVersion = 8; // スキーマバージョンを定義

        public DatabaseManager()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Illustra");

            // アプリケーションデータフォルダを作成
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            _dbPath = Path.Combine(appDataPath, "illustra.db");
            _connectionString = $"Data Source={_dbPath};Version=3;";
            _dataProvider = SQLiteTools.GetDataProvider("SQLite");
            _dbAccess = new DatabaseAccess(_dataProvider, _connectionString);

            // LinqToDBの設定を追加
            DataConnection.DefaultSettings = new MySettings(_connectionString);

            // データベースとテーブルを初期化
            InitializeDatabase();
        }

        /// <summary>
        /// デバッグログを有効化します
        /// </summary>
        /// <param name="enable">デバッグログを有効にするかどうか</param>
        public void EnableDebugLogging(bool enable)
        {
            // DatabaseAccessクラスのデバッグログ設定を変更
            _dbAccess.EnableDebugLogging = enable;
            _dbAccess.EnableVerboseLogging = false; // 詳細ログは常に無効

            // 設定状態をログに出力
            if (enable)
            {
                Debug.WriteLine("[DatabaseManager] デバッグログを有効化しました");
            }
            else
            {
                Debug.WriteLine("[DatabaseManager] デバッグログを無効化しました");
            }
        }

        /// <summary>
        /// 最近のデータベースアクセスログを取得します
        /// </summary>
        /// <returns>ログの文字列</returns>
        public string GetDatabaseLogs()
        {
            return _dbAccess.GetRecentLogs();
        }

        /// <summary>
        /// 現在のデータベース操作の状態を取得します
        /// </summary>
        /// <returns>アクティブな読み取り操作数、アクティブな書き込み操作数、待機中の書き込み操作数</returns>
        public (int ActiveReads, int ActiveWrites, int PendingWrites) GetDatabaseOperationStatus()
        {
            return _dbAccess.GetOperationStatus();
        }

        private void InitializeDatabase()
        {
            var db = new DataConnection(_dataProvider, _connectionString);

            // スキーマバージョンを確認
            int schemaVersion = GetSchemaVersion(db);
            if (schemaVersion != CurrentSchemaVersion)
            {
                // テーブルを削除して再作成
                db.DropTable<FileNodeModel>(throwExceptionIfNotExists: false);

                db.CreateTable<FileNodeModel>();

                // FileNodeModel テーブルの作成
                db.Execute(@"
                    CREATE INDEX idx_FolderPath ON FileNodeModel(FolderPath);
                    CREATE INDEX idx_FileName ON FileNodeModel(FileName);
                    CREATE INDEX idx_CreationTime ON FileNodeModel(CreationTime);
                    CREATE INDEX idx_LastModified ON FileNodeModel(LastModified);
                    CREATE INDEX idx_Rating ON FileNodeModel(Rating);");
                Debug.WriteLine("Created new FileNodeModel table");

                // スキーマバージョンを更新
                SetSchemaVersion(db, CurrentSchemaVersion);
            }
        }

        private int GetSchemaVersion(DataConnection db)
        {
            return db.Execute<int>("PRAGMA user_version");
        }

        private void SetSchemaVersion(DataConnection db, int version)
        {
            db.Execute($"PRAGMA user_version = {version}");
        }

        public async Task SaveFileNodeAsync(FileNodeModel fileNode)
        {
            await _dbAccess.WriteAsync(async db =>
            {
                await db.InsertOrReplaceAsync(fileNode);
            });
        }

        public async Task<List<FileNodeModel>> GetFileNodesAsync(string folderPath, CancellationToken cancellationToken = default)
        {
            var sw = new Stopwatch();
            sw.Start();
            var result = await _dbAccess.ReadAsync(async db =>
            {
                return await db.GetTable<FileNodeModel>()
                             .Where(fn => fn.FolderPath == folderPath)
                             .ToListAsync();
            }, cancellationToken);
            sw.Stop();
            return result;
        }

        public async Task<FileNodeModel?> GetFileNodeAsync(string fullPath, CancellationToken cancellationToken = default)
        {
            var sw = new Stopwatch();
            sw.Start();
            var result = await _dbAccess.ReadAsync(async db =>
            {
                return await db.GetTable<FileNodeModel>()
                             .FirstOrDefaultAsync(fn => fn.FullPath == fullPath);
            }, cancellationToken);
            sw.Stop();
            return result;
        }

        public async Task<List<FileNodeModel>> GetSortedFileNodesAsync(string folderPath, bool sortByDate, bool sortAscending, CancellationToken cancellationToken = default)
        {
            var sw = new Stopwatch();
            sw.Start();
            var result = await _dbAccess.ReadAsync(async db =>
            {
                var query = db.GetTable<FileNodeModel>().Where(fn => fn.FolderPath == folderPath);

                if (sortByDate)
                {
                    query = sortAscending ? query.OrderBy(fn => fn.CreationTime) : query.OrderByDescending(fn => fn.CreationTime);
                }
                else
                {
                    query = sortAscending ? query.OrderBy(fn => fn.FileName) : query.OrderByDescending(fn => fn.FileName);
                }

                return await query.ToListAsync();
            }, cancellationToken);
            sw.Stop();
            return result;
        }

        public async Task UpdateRatingAsync(string fullPath, int rating)
        {
            await _dbAccess.WriteAsync(async db =>
            {
                await db.GetTable<FileNodeModel>()
                        .Where(fn => fn.FullPath == fullPath)
                        .Set(fn => fn.Rating, rating)
                        .UpdateAsync();
            });
        }

        public async Task SaveFileNodesBatchAsync(IEnumerable<FileNodeModel> fileNodes)
        {
            await _dbAccess.WriteAsync(async db =>
            {
                try
                {
                    await db.BulkCopyAsync(fileNodes);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SaveFileNodesBatchAsync中にエラーが発生しました: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<List<FileNodeModel>> GetOrCreateFileNodesAsync(string folderPath, Func<string, bool> fileFilter, CancellationToken cancellationToken = default)
        {
            var sw = new Stopwatch();
            sw.Start();

            try
            {
                // 既存のファイルノードを取得
                var existingNodes = await GetFileNodesAsync(folderPath, cancellationToken);
                var existingNodeDict = existingNodes.ToDictionary(n => n.FullPath, n => n);

                // キャンセル確認
                cancellationToken.ThrowIfCancellationRequested();

                // ディレクトリからファイルパスを列挙
                var filePaths = Directory.EnumerateFiles(folderPath).Where(FileHelper.IsImageFile).ToList();
                var newNodes = new List<FileNodeModel>(filePaths.Count);
                Debug.WriteLine($"ファイル '{folderPath}: ファイル列挙 {sw.ElapsedMilliseconds} ms");

                // 最新のファイルノードを作成
                foreach (var filePath in filePaths)
                {
                    try
                    {
                        var node = new FileNodeModel(filePath);
                        newNodes.Add(node);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ファイル '{filePath}' のノード作成中にエラー: {ex.Message}");
                    }
                }
                Debug.WriteLine($"ファイル '{folderPath}: ファイルノードを作成 {sw.ElapsedMilliseconds} ms");

                // Note: Write操作のキャンセルサポートは将来の検討課題とする
                // 現在はWrite操作開始後のキャンセルは許可しない設計
                await _dbAccess.WriteWithTransactionAsync(async db =>
                {
                    // DB上から該当フォルダのレコードを削除
                    await db.GetTable<FileNodeModel>()
                           .Where(fn => fn.FolderPath == folderPath)
                           .DeleteAsync();

                    // 既存ノードのレーティングを維持
                    foreach (var node in newNodes)
                    {
                        if (existingNodeDict.TryGetValue(node.FullPath, out var existingNode))
                        {
                            if (node.Rating != existingNode.Rating)
                            {
                                node.Rating = existingNode.Rating;
                            }
                        }
                    }

                    // 新規ノードをバッチ保存
                    if (newNodes.Count > 0)
                    {
                        await db.BulkCopyAsync(newNodes);
                    }
                });

                Debug.WriteLine($"ファイル '{folderPath}: バッチ保存 {sw.ElapsedMilliseconds} ms");
                sw.Stop();

                return newNodes;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"GetOrCreateFileNodesAsync was cancelled for folder: {folderPath}");
                throw;
            }
            catch (SqliteException ex)
            {
                Debug.WriteLine($"GetOrCreateFileNodesAsync中にSQLエラーが発生しました: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetOrCreateFileNodesAsync中にエラーが発生しました: {ex.Message}");
                throw;
            }
        }

        public async Task<FileNodeModel> HandleFileRenamedAsync(string oldPath, string newPath, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // まずDBから古いパスのノードを探す
                var fileNode = await GetFileNodeAsync(oldPath, cancellationToken);

                // 古いパスのノードが見つからない場合は新規作成
                if (fileNode == null)
                {
                    return await CreateFileNodeAsync(newPath, cancellationToken);
                }

                // ファイル情報を更新
                var fileInfo = new FileInfo(newPath);
                if (!fileInfo.Exists)
                {
                    Debug.WriteLine($"New file not found: {newPath}");
                    return null;
                }

                // 新しいパスの情報で更新
                fileNode.FullPath = newPath;
                fileNode.FolderPath = Path.GetDirectoryName(newPath);
                fileNode.FileName = Path.GetFileName(newPath);
                fileNode.LastModified = fileInfo.LastWriteTime;

                // DBを更新
                await UpdateFileNode(fileNode);
                return fileNode;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"HandleFileRenamedAsync was cancelled for {oldPath} -> {newPath}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling renamed file {oldPath} -> {newPath}: {ex.Message}");
                return null;
            }
        }

        public async Task<FileNodeModel> CreateFileNodeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var folderPath = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // ファイル情報を取得
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    Debug.WriteLine($"File not found: {filePath}");
                    return null;
                }

                // 新規ファイルノードを作成
                var newNode = new FileNodeModel
                {
                    FullPath = filePath,
                    FolderPath = folderPath,
                    FileName = fileName,
                    CreationTime = fileInfo.CreationTime,
                    LastModified = fileInfo.LastWriteTime,
                    Rating = 0
                };

                // DBに保存
                await SaveFileNodeAsync(newNode);
                return newNode;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"CreateFileNodeAsync was cancelled for file: {filePath}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating file node for {filePath}: {ex.Message}");
                return null;
            }
        }

        public async Task UpdateFileNode(FileNodeModel fileNode)
        {
            await _dbAccess.WriteAsync(async db =>
            {
                await db.UpdateAsync(fileNode);
            });
        }

        public async Task DeleteFileNodeAsync(string fullPath)
        {
            await _dbAccess.WriteAsync(async db =>
            {
                await db.GetTable<FileNodeModel>()
                        .Where(fn => fn.FullPath == fullPath)
                        .DeleteAsync();
            });
        }
    }

    public class MySettings : ILinqToDBSettings
    {
        private readonly string _connectionString;

        public MySettings(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IEnumerable<IDataProviderSettings> DataProviders => Enumerable.Empty<IDataProviderSettings>();

        public string DefaultConfiguration => "SQLite";
        public string DefaultDataProvider => "SQLite";

        public IEnumerable<IConnectionStringSettings> ConnectionStrings
        {
            get
            {
                yield return new ConnectionStringSettings("SQLite", "SQLite", _connectionString);
            }
        }
    }
}
