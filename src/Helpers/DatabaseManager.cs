using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Illustra.Models;
using System.Diagnostics;
using System.Linq;
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
        private const int MaxRetryCount = 5;
        private const int RetryDelayMilliseconds = 200;
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

            // LinqToDBの設定を追加
            DataConnection.DefaultSettings = new MySettings(_connectionString);

            // データベースとテーブルを初期化
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            var db = new DataConnection(_dataProvider, _connectionString);

            // スキーマバージョンを確認
            int schemaVersion = GetSchemaVersion(db);
            Debug.WriteLine($"Current schema version: {schemaVersion}");
            if (schemaVersion != CurrentSchemaVersion)
            {
                // テーブルを削除して再作成
                db.DropTable<FileNodeModel>(throwExceptionIfNotExists: false);
                Debug.WriteLine("Dropped existing FileNodeModel table");

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
                Debug.WriteLine($"Updated schema version to {CurrentSchemaVersion}");
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
            await ExecuteWithRetryAsync(async () =>
            {
                using var db = new DataConnection(_dataProvider, _connectionString);
                await db.InsertOrReplaceAsync(fileNode);
            });
        }

        public async Task<List<FileNodeModel>> GetFileNodesAsync(string folderPath)
        {
            var sw = new Stopwatch();
            sw.Start();
            using var db = new DataConnection(_dataProvider, _connectionString);
            var result = await db.GetTable<FileNodeModel>().Where(fn => fn.FolderPath == folderPath).ToListAsync();
            sw.Stop();
            Debug.WriteLine($"GetFileNodesAsync executed in {sw.ElapsedMilliseconds} ms");
            return result;
        }

        public async Task<FileNodeModel?> GetFileNodeAsync(string fullPath)
        {
            var sw = new Stopwatch();
            sw.Start();
            using var db = new DataConnection(_dataProvider, _connectionString);
            var result = await db.GetTable<FileNodeModel>().FirstOrDefaultAsync(fn => fn.FullPath == fullPath);
            sw.Stop();
            Debug.WriteLine($"GetFileNodeAsync executed in {sw.ElapsedMilliseconds} ms");
            return result;
        }

        public async Task<List<FileNodeModel>> GetSortedFileNodesAsync(string folderPath, bool sortByDate, bool sortAscending)
        {
            var sw = new Stopwatch();
            sw.Start();
            using var db = new DataConnection(_dataProvider, _connectionString);
            var query = db.GetTable<FileNodeModel>().Where(fn => fn.FolderPath == folderPath);

            if (sortByDate)
            {
                query = sortAscending ? query.OrderBy(fn => fn.CreationTime) : query.OrderByDescending(fn => fn.CreationTime);
            }
            else
            {
                query = sortAscending ? query.OrderBy(fn => fn.FileName) : query.OrderByDescending(fn => fn.FileName);
            }

            var result = await query.ToListAsync();
            sw.Stop();
            Debug.WriteLine($"GetSortedFileNodesAsync executed in {sw.ElapsedMilliseconds} ms");
            return result;
        }

        public async Task<List<FileNodeModel>> GetFileNodesByRatingAsync(string folderPath, int rating)
        {
            var sw = new Stopwatch();
            sw.Start();
            using var db = new DataConnection(_dataProvider, _connectionString);
            var result = await db.GetTable<FileNodeModel>().Where(fn => fn.FolderPath == folderPath && fn.Rating == rating).ToListAsync();
            sw.Stop();
            Debug.WriteLine($"GetFileNodesByRatingAsync executed in {sw.ElapsedMilliseconds} ms");
            return result;
        }

        public async Task UpdateRatingAsync(string fullPath, int rating)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var db = new DataConnection(_dataProvider, _connectionString);
                await db.GetTable<FileNodeModel>().Where(fn => fn.FullPath == fullPath).Set(fn => fn.Rating, rating).UpdateAsync();
            });
        }

        public async Task SaveFileNodesBatchAsync(IEnumerable<FileNodeModel> fileNodes)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    using var db = new DataConnection(_dataProvider, _connectionString);
                    await db.BulkCopyAsync(fileNodes);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SaveFileNodesBatchAsync中にエラーが発生しました: {ex.Message}");
                }
            });
        }

        public async Task<List<FileNodeModel>> GetOrCreateFileNodesAsync(string folderPath, Func<string, bool> fileFilter)
        {
            var sw = new Stopwatch();
            sw.Start();

            Debug.WriteLine("Start GetOrCreateFileNodesAsync");

            try
            {
                // 既存のファイルノードを取得
                var existingNodes = await GetFileNodesAsync(folderPath);
                var existingNodeDict = existingNodes.ToDictionary(n => n.FullPath, n => n);

                Debug.WriteLine($"既存ノード取得: {sw.ElapsedMilliseconds}ms");
                Debug.WriteLine($"既存ノード数: {existingNodes.Count}");

                // ディレクトリからファイルパスを列挙
                var filePaths = Directory.EnumerateFiles(folderPath).Where(FileHelper.IsImageFile).ToList();
                var newNodes = new List<FileNodeModel>(filePaths.Count);

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

                Debug.WriteLine($"最新ノード作成: {sw.ElapsedMilliseconds}ms");

                // DB上から該当フォルダのレコードを削除
                await ExecuteWithRetryAsync(async () =>
                {
                    using var db = new DataConnection(_dataProvider, _connectionString);
                    await db.GetTable<FileNodeModel>().Where(fn => fn.FolderPath == folderPath).DeleteAsync();
                });

                Debug.WriteLine($"既存ノード削除: {sw.ElapsedMilliseconds}ms");
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
                    await SaveFileNodesBatchAsync(newNodes);
                }

                Debug.WriteLine($"新規ノードバッチ保存: {sw.ElapsedMilliseconds}ms");


                sw.Stop();
                Debug.WriteLine($"GetOrCreateFileNodes処理時間: {sw.ElapsedMilliseconds}ms");

                return newNodes;
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

        public async Task<FileNodeModel> HandleFileRenamedAsync(string oldPath, string newPath)
        {
            try
            {
                // まずDBから古いパスのノードを探す
                var fileNode = await GetFileNodeAsync(oldPath);

                // 古いパスのノードが見つからない場合は新規作成
                if (fileNode == null)
                {
                    return await CreateFileNodeAsync(newPath);
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling renamed file {oldPath} -> {newPath}: {ex.Message}");
                return null;
            }
        }

        public async Task<FileNodeModel> CreateFileNodeAsync(string filePath)
        {
            var folderPath = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);

            try
            {
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating file node for {filePath}: {ex.Message}");
                return null;
            }
        }

        public async Task<int> GetRatingAsync(string filePath)
        {
            using var db = new DataConnection(_connectionString);
            return await db.GetTable<FileNodeModel>().Where(fn => fn.FullPath == filePath).Select(fn => fn.Rating).FirstOrDefaultAsync();
        }

        public async Task UpdateFileNode(FileNodeModel fileNode)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var db = new DataConnection(_dataProvider, _connectionString);
                await db.UpdateAsync(fileNode);
            });
        }

        private async Task ExecuteWithRetryAsync(Func<Task> operation)
        {
            int retryCount = 0;
            var sw = new Stopwatch();
            while (true)
            {
                try
                {
                    sw.Start();
                    await operation();
                    sw.Stop();
                    Debug.WriteLine($"Query executed in {sw.ElapsedMilliseconds} ms");
                    break;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5) // SQLite Error 5: 'database is locked'
                {
                    retryCount++;
                    if (retryCount > MaxRetryCount)
                    {
                        throw;
                    }
                    int delay = RetryDelayMilliseconds * (int)Math.Pow(2, retryCount - 1); // Exponential backoff
                    Debug.WriteLine($"Database is locked, retrying in {delay} ms");
                    await Task.Delay(delay);
                }
                finally
                {
                    sw.Reset();
                }
            }
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
