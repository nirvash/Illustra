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
        private const int CurrentSchemaVersion = 4; // スキーマバージョンを定義

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
            using var db = new DataConnection(_dataProvider, _connectionString);
            return await db.GetTable<FileNodeModel>().Where(fn => fn.FolderPath == folderPath).ToListAsync();
        }

        public async Task<FileNodeModel?> GetFileNodeAsync(string fullPath)
        {
            using var db = new DataConnection(_dataProvider, _connectionString);
            return await db.GetTable<FileNodeModel>().FirstOrDefaultAsync(fn => fn.FullPath == fullPath);
        }

        public async Task<List<FileNodeModel>> GetSortedFileNodesAsync(string folderPath, bool sortByDate, bool sortAscending)
        {
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

            return await query.ToListAsync();
        }

        public async Task<List<FileNodeModel>> GetFileNodesByRatingAsync(string folderPath, int rating)
        {
            using var db = new DataConnection(_dataProvider, _connectionString);
            return await db.GetTable<FileNodeModel>().Where(fn => fn.FolderPath == folderPath && fn.Rating == rating).ToListAsync();
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
                using var db = new DataConnection(_dataProvider, _connectionString);
                await db.BulkCopyAsync(fileNodes);
            });
        }

        public async Task<List<FileNodeModel>> GetOrCreateFileNodesAsync(string folderPath, IEnumerable<string> filePaths)
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

                // 新規作成が必要なファイルを特定
                var newFilePaths = filePaths.Where(path => !existingNodeDict.ContainsKey(path)).ToList();
                var newNodes = new List<FileNodeModel>(newFilePaths.Count);

                // 新規ファイルノードを作成
                foreach (var filePath in newFilePaths)
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        var node = new FileNodeModel(filePath)
                        {
                            CreationTime = fileInfo.CreationTime,
                            LastModified = fileInfo.LastWriteTime,
                            FileSize = fileInfo.Length,
                            FileType = Path.GetExtension(filePath),
                            IsImage = true
                        };
                        newNodes.Add(node);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ファイル '{filePath}' のノード作成中にエラー: {ex.Message}");
                    }
                }

                Debug.WriteLine($"新規ノード作成: {sw.ElapsedMilliseconds}ms");

                // 新規ノードをバッチ保存
                if (newNodes.Count > 0)
                {
                    await SaveFileNodesBatchAsync(newNodes);
                }

                Debug.WriteLine($"新規ノードバッチ保存: {sw.ElapsedMilliseconds}ms");

                // 全ノードを返す
                var allNodes = existingNodes.Concat(newNodes).ToList();

                sw.Stop();
                Debug.WriteLine($"GetOrCreateFileNodes処理時間: {sw.ElapsedMilliseconds}ms");

                return allNodes;
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

        public async Task<int> GetRatingAsync(string filePath)
        {
            using var db = new DataConnection(_connectionString);
            return await db.GetTable<FileNodeModel>().Where(fn => fn.FullPath == filePath).Select(fn => fn.Rating).FirstOrDefaultAsync();
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
                    if (retryCount > MaxRetryCount)
                    {
                        throw;
                    }
                    await Task.Delay(RetryDelayMilliseconds);
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
