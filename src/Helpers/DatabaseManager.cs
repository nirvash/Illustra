using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Illustra.Models;
using System.Diagnostics;
using System.Linq;

namespace Illustra.Helpers
{
    public class DatabaseManager
    {
        private readonly string _connectionString;
        private readonly string _dbPath;
        private const int MaxRetryCount = 5;
        private const int RetryDelayMilliseconds = 200;
        private const int CurrentSchemaVersion = 1; // スキーマバージョンを定義

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
            _connectionString = $"Data Source={_dbPath}";

            // データベースとテーブルを初期化
            InitializeDatabase();
        }

        /// <summary>
        /// データベースとテーブルを初期化します
        /// </summary>
        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // スキーマバージョンを確認
            int schemaVersion = GetSchemaVersion(connection);
            if (schemaVersion != CurrentSchemaVersion)
            {
                // テーブルを削除して再作成
                using var dropCommand = connection.CreateCommand();
                dropCommand.CommandText = "DROP TABLE IF EXISTS FileNodes";
                dropCommand.ExecuteNonQuery();

                // FileNodes テーブルの作成
                using var createCommand = connection.CreateCommand();
                createCommand.CommandText = @"
                    CREATE TABLE FileNodes (
                        FullPath TEXT PRIMARY KEY,
                        FileName TEXT NOT NULL,
                        FolderPath TEXT NOT NULL,
                        FileSize INTEGER NOT NULL,
                        CreationTime TEXT NOT NULL,
                        LastModified TEXT NOT NULL,
                        FileType TEXT,
                        Rating INTEGER DEFAULT 0,
                        IsImage INTEGER DEFAULT 0
                    );
                    CREATE INDEX idx_FolderPath ON FileNodes(FolderPath);
                    CREATE INDEX idx_FileName ON FileNodes(FileName);
                    CREATE INDEX idx_CreationTime ON FileNodes(CreationTime);
                    CREATE INDEX idx_LastModified ON FileNodes(LastModified);
                    CREATE INDEX idx_Rating ON FileNodes(Rating);";
                createCommand.ExecuteNonQuery();

                // スキーマバージョンを更新
                SetSchemaVersion(connection, CurrentSchemaVersion);
            }
        }

        private int GetSchemaVersion(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private void SetSchemaVersion(SqliteConnection connection, int version)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {version}";
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// ファイルノード情報をデータベースに挿入または更新します
        /// </summary>
        public async Task SaveFileNodeAsync(FileNodeModel fileNode)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT OR REPLACE INTO FileNodes
                    (FullPath, FileName, FolderPath, FileSize, CreationTime, LastModified, FileType, Rating, IsImage)
                    VALUES
                    (@FullPath, @FileName, @FolderPath, @FileSize, @CreationTime, @LastModified, @FileType, @Rating, @IsImage)";

                command.Parameters.AddWithValue("@FullPath", fileNode.FullPath);
                command.Parameters.AddWithValue("@FileName", fileNode.Name);
                command.Parameters.AddWithValue("@FolderPath", Path.GetDirectoryName(fileNode.FullPath));
                command.Parameters.AddWithValue("@FileSize", fileNode.FileSize);
                command.Parameters.AddWithValue("@CreationTime", fileNode.CreationTime.ToString("o")); // ISO 8601形式
                command.Parameters.AddWithValue("@LastModified", fileNode.LastModified.ToString("o")); // ISO 8601形式
                command.Parameters.AddWithValue("@FileType", fileNode.FileType);
                command.Parameters.AddWithValue("@Rating", fileNode.Rating);
                command.Parameters.AddWithValue("@IsImage", fileNode.IsImage ? 1 : 0);

                await command.ExecuteNonQueryAsync();
            });
        }

        /// <summary>
        /// フォルダパスに含まれるすべてのファイルノード情報を取得します
        /// </summary>
        public async Task<List<FileNodeModel>> GetFileNodesAsync(string folderPath)
        {
            var fileNodes = new List<FileNodeModel>();

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM FileNodes WHERE FolderPath = @FolderPath ORDER BY FileName";
            command.Parameters.AddWithValue("@FolderPath", folderPath);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                // デフォルトコンストラクタを使用してインスタンスを作成
                var fileNode = new FileNodeModel
                {
                    FullPath = reader.GetString(0),
                    FileName = reader.GetString(1),
                    // FolderPathはデータベースには保存されているが、プロパティとしては計算される
                    FileSize = reader.GetInt64(3),
                    CreationTime = DateTime.Parse(reader.GetString(4)),
                    LastModified = DateTime.Parse(reader.GetString(5)),
                    FileType = reader.GetString(6),
                    Rating = reader.GetInt32(7),
                    IsImage = reader.GetInt32(8) == 1
                };

                // サムネイル情報は別途ロードする必要がある
                fileNode.ThumbnailInfo = new ThumbnailInfo(null, ThumbnailState.NotLoaded);

                fileNodes.Add(fileNode);
            }

            return fileNodes;
        }

        /// <summary>
        /// 指定したフルパスのファイルノード情報を取得します
        /// </summary>
        public async Task<FileNodeModel?> GetFileNodeAsync(string fullPath)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM FileNodes WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", fullPath);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                // デフォルトコンストラクタを使用してインスタンスを作成
                var fileNode = new FileNodeModel
                {
                    FullPath = reader.GetString(0),
                    FileName = reader.GetString(1),
                    // FolderPathはデータベースには保存されているが、プロパティとしては計算される
                    FileSize = reader.GetInt64(3),
                    CreationTime = DateTime.Parse(reader.GetString(4)),
                    LastModified = DateTime.Parse(reader.GetString(5)),
                    FileType = reader.GetString(6),
                    Rating = reader.GetInt32(7),
                    IsImage = reader.GetInt32(8) == 1
                };

                // サムネイル情報は別途ロードする必要がある
                fileNode.ThumbnailInfo = new ThumbnailInfo(null, ThumbnailState.NotLoaded);

                return fileNode;
            }

            return null;
        }

        /// <summary>
        /// フォルダ内のファイルをソート基準に従ってソートした結果を取得します
        /// </summary>
        public async Task<List<FileNodeModel>> GetSortedFileNodesAsync(
            string folderPath, bool sortByDate, bool sortAscending)
        {
            var fileNodes = new List<FileNodeModel>();

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();

            // ソート条件の設定
            string orderBy;
            if (sortByDate)
            {
                orderBy = sortAscending ? "CreationTime ASC" : "CreationTime DESC";
            }
            else
            {
                orderBy = sortAscending ? "FileName ASC" : "FileName DESC";
            }

            command.CommandText = $"SELECT * FROM FileNodes WHERE FolderPath = @FolderPath ORDER BY {orderBy}";
            command.Parameters.AddWithValue("@FolderPath", folderPath);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var fileNode = new FileNodeModel
                {
                    FullPath = reader.GetString(0),
                    FileName = reader.GetString(1),
                    FileSize = reader.GetInt64(3),
                    CreationTime = DateTime.Parse(reader.GetString(4)),
                    LastModified = DateTime.Parse(reader.GetString(5)),
                    FileType = reader.GetString(6),
                    Rating = reader.GetInt32(7),
                    IsImage = reader.GetInt32(8) == 1,
                    ThumbnailInfo = new ThumbnailInfo(null, ThumbnailState.NotLoaded)
                };

                fileNodes.Add(fileNode);
            }

            return fileNodes;
        }

        /// <summary>
        /// レーティングでファイルをフィルタリングします
        /// </summary>
        public async Task<List<FileNodeModel>> GetFileNodesByRatingAsync(string folderPath, int rating)
        {
            var fileNodes = new List<FileNodeModel>();

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM FileNodes WHERE FolderPath = @FolderPath AND Rating = @Rating";
            command.Parameters.AddWithValue("@FolderPath", folderPath);
            command.Parameters.AddWithValue("@Rating", rating);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var fileNode = new FileNodeModel
                {
                    FullPath = reader.GetString(0),
                    FileName = reader.GetString(1),
                    FileSize = reader.GetInt64(3),
                    CreationTime = DateTime.Parse(reader.GetString(4)),
                    LastModified = DateTime.Parse(reader.GetString(5)),
                    FileType = reader.GetString(6),
                    Rating = reader.GetInt32(7),
                    IsImage = reader.GetInt32(8) == 1,
                    ThumbnailInfo = new ThumbnailInfo(null, ThumbnailState.NotLoaded)
                };

                fileNodes.Add(fileNode);
            }

            return fileNodes;
        }

        /// <summary>
        /// レーティングを更新してデータベースに保存します
        /// </summary>
        public async Task UpdateRatingAsync(string fullPath, int rating)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE FileNodes
                    SET Rating = @Rating
                    WHERE FullPath = @FullPath;

                    INSERT INTO FileNodes (FullPath, Rating)
                    SELECT @FullPath, @Rating
                    WHERE NOT EXISTS (SELECT 1 FROM FileNodes WHERE FullPath = @FullPath);";

                command.Parameters.AddWithValue("@FullPath", fullPath);
                command.Parameters.AddWithValue("@Rating", rating);

                await command.ExecuteNonQueryAsync();
            });
        }

        /// <summary>
        /// 複数のファイルノードをバッチで保存します（高速化のため）
        /// </summary>
        /// <param name="fileNodes">保存するファイルノードのリスト</param>
        /// <returns>Task</returns>
        public async Task SaveFileNodesBatchAsync(IEnumerable<FileNodeModel> fileNodes)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                var sw = new Stopwatch();
                sw.Start();

                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // トランザクションを開始して一括処理
                using var transaction = connection.BeginTransaction();

                try
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"
                        INSERT OR REPLACE INTO FileNodes
                        (FullPath, FileName, FolderPath, FileSize, CreationTime, LastModified, FileType, Rating, IsImage)
                        VALUES
                        (@FullPath, @FileName, @FolderPath, @FileSize, @CreationTime, @LastModified, @FileType, @Rating, @IsImage)";

                    // パラメータを準備
                    var pathParam = command.CreateParameter();
                    pathParam.ParameterName = "@FullPath";
                    command.Parameters.Add(pathParam);

                    var nameParam = command.CreateParameter();
                    nameParam.ParameterName = "@FileName";
                    command.Parameters.Add(nameParam);

                    var folderParam = command.CreateParameter();
                    folderParam.ParameterName = "@FolderPath";
                    command.Parameters.Add(folderParam);

                    var sizeParam = command.CreateParameter();
                    sizeParam.ParameterName = "@FileSize";
                    command.Parameters.Add(sizeParam);

                    var creationParam = command.CreateParameter();
                    creationParam.ParameterName = "@CreationTime";
                    command.Parameters.Add(creationParam);

                    var modifiedParam = command.CreateParameter();
                    modifiedParam.ParameterName = "@LastModified";
                    command.Parameters.Add(modifiedParam);

                    var typeParam = command.CreateParameter();
                    typeParam.ParameterName = "@FileType";
                    command.Parameters.Add(typeParam);

                    var ratingParam = command.CreateParameter();
                    ratingParam.ParameterName = "@Rating";
                    command.Parameters.Add(ratingParam);

                    var imageParam = command.CreateParameter();
                    imageParam.ParameterName = "@IsImage";
                    command.Parameters.Add(imageParam);

                    // 一括でInsertを実行
                    foreach (var fileNode in fileNodes)
                    {
                        pathParam.Value = fileNode.FullPath;
                        nameParam.Value = fileNode.Name;
                        folderParam.Value = Path.GetDirectoryName(fileNode.FullPath) ?? string.Empty;
                        sizeParam.Value = fileNode.FileSize;
                        creationParam.Value = fileNode.CreationTime.ToString("o"); // ISO 8601形式
                        modifiedParam.Value = fileNode.LastModified.ToString("o"); // ISO 8601形式
                        typeParam.Value = fileNode.FileType;
                        ratingParam.Value = fileNode.Rating;
                        imageParam.Value = fileNode.IsImage ? 1 : 0;

                        await command.ExecuteNonQueryAsync();
                    }

                    // トランザクションをコミット
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"バッチ保存中にエラーが発生しました: {ex.Message}");
                    transaction.Rollback();
                    throw;
                }

                sw.Stop();
                Debug.WriteLine($"バッチ保存処理時間: {sw.ElapsedMilliseconds}ms");
            });
        }

        /// <summary>
        /// 指定フォルダ内の既存ファイルノードを取得し、新規ファイルのみをバッチ保存します
        /// </summary>
        /// <param name="folderPath">フォルダパス</param>
        /// <param name="newFilePaths">新規ファイルパスのリスト</param>
        /// <returns>既存のファイルノードと新規作成したファイルノードを含む全ノードのリスト</returns>
        public async Task<List<FileNodeModel>> GetOrCreateFileNodesAsync(string folderPath, IEnumerable<string> filePaths)
        {
            var sw = new Stopwatch();
            sw.Start();

            Debug.WriteLine("Start GetOrCreateFileNodesAsync");

            // 既存のファイルノードを取得
            var existingNodes = await GetFileNodesAsync(folderPath);
            var existingNodeDict = existingNodes.ToDictionary(n => n.FullPath, n => n);

            Debug.WriteLine($"既存ノード取得: {sw.ElapsedMilliseconds}ms");

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

        public async Task<int> GetRatingAsync(string filePath)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Rating FROM FileNodes WHERE FullPath = @FilePath";
            command.Parameters.AddWithValue("@FilePath", filePath);

            var result = await command.ExecuteScalarAsync();
            return result != null ? Convert.ToInt32(result) : 0;
        }

        private async Task CreateTablesAsync(SqliteConnection connection)
        {
            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS FileNodes (
                    FullPath TEXT PRIMARY KEY,
                    FileName TEXT NOT NULL,
                    FolderPath TEXT NOT NULL,
                    FileSize INTEGER NOT NULL,
                    CreationTime TEXT NOT NULL,
                    LastModified TEXT NOT NULL,
                    FileType TEXT,
                    Rating INTEGER DEFAULT 0,
                    IsImage INTEGER DEFAULT 0
                );

                CREATE INDEX IF NOT EXISTS idx_rating ON FileNodes(Rating);";

            await command.ExecuteNonQueryAsync();
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
}
