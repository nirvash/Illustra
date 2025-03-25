using System.IO;
using Microsoft.Data.Sqlite;
using Illustra.Models;
using System.Diagnostics;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Configuration;
using LinqToDB.DataProvider.SQLite;
using LinqToDB.DataProvider;
using System.Windows;

namespace Illustra.Helpers
{
    public class DatabaseManager
    {
        private readonly string _connectionString;
        private readonly IDataProvider _dataProvider;
        private readonly string _dbPath;
        private readonly DatabaseAccess _dbAccess;
        private const int CurrentSchemaVersion = 9; // スキーマバージョンを定義
        // 8 -> 9: FileNodeModelに LastCheckedTime を追加

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
            if (schemaVersion < 8)
            {
                CreateCurrentSchemaTables(db);
            }
            else if (schemaVersion == 8)
            {
                AlterSchemaFrom8to9(db);
            }
        }

        private void AlterSchemaFrom8to9(DataConnection db)
        {
            db.Execute(@"ALTER TABLE FileNodeModel ADD COLUMN LastCheckedTime DateTime2;");
            db.Execute(@"CREATE INDEX IF NOT EXISTS idx_LastCheckedTime ON FileNodeModel(LastCheckedTime);");
            Debug.WriteLine("Migrated FileNodeModel table (version 8 to 9)");

            // ここは 9 を指定。次の更新では最新ではなくなるので注意
            SetSchemaVersion(db, 9);
        }

        private void CreateCurrentSchemaTables(DataConnection db)
        {
            // 既存のテーブルを削除して新規作成
            db.DropTable<FileNodeModel>(throwExceptionIfNotExists: false);
            db.CreateTable<FileNodeModel>();

            // FileNodeModel テーブルの作成
            db.Execute(@"
                CREATE INDEX idx_FolderPath ON FileNodeModel(FolderPath);
                CREATE INDEX idx_FileName ON FileNodeModel(FileName);
                CREATE INDEX idx_CreationTime ON FileNodeModel(CreationTime);
                CREATE INDEX idx_LastModified ON FileNodeModel(LastModified);
                CREATE INDEX idx_Rating ON FileNodeModel(Rating);
                CREATE INDEX idx_LastCheckedTime ON FileNodeModel(LastCheckedTime);
            ");

            Debug.WriteLine("Created new FileNodeModel table");

            // カレントのスキーマバージョンを指定
            SetSchemaVersion(db, CurrentSchemaVersion);
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

        public async Task UpdateRatingAsync(FileNodeModel fileNode)
        {
            await _dbAccess.WriteAsync(async db =>
            {
                try
                {
                    // 最後にチェックした時刻を設定
                    fileNode.LastCheckedTime = DateTime.Now;

                    if (fileNode.Rating <= 0)
                    {
                        // 1. `DeleteAsync` を実行
                        int affectedRows = await db.GetTable<FileNodeModel>()
                            .Where(fn => fn.FullPath == fileNode.FullPath)
                            .DeleteAsync();
                    }
                    else
                    {
                        // 1. `UpdateAsync` を実行し、更新された行数を取得
                        int affectedRows = await db.GetTable<FileNodeModel>()
                            .Where(fn => fn.FullPath == fileNode.FullPath)
                            .Set(fn => fn.Rating, fileNode.Rating)
                            .Set(fn => fn.LastCheckedTime, fileNode.LastCheckedTime)
                            .UpdateAsync();

                        // 2. 更新された行が 0 の場合は `InsertAsync` を実行
                        if (affectedRows == 0)
                        {
                            await db.InsertAsync(fileNode);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogError($"Error updating rating for {fileNode.FullPath}: {ex.Message}");
                    var _appSettings = SettingsHelper.GetSettings();
                    Debug.WriteLine($"Error updating rating for {fileNode.FullPath}: {ex.Message}");
                    if (_appSettings.DeveloperMode)
                    {
                        MessageBox.Show(
                        string.Format(
                            "データベースへの保存に失敗しました: {0} {1}",
                            fileNode.FullPath,
                            ex.Message),
                        "エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    }
                }
            });
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

        public async Task DeleteFolderFileNodesAsync(string folderPath)
        {
            await _dbAccess.WriteAsync(async db =>
            {
                await db.GetTable<FileNodeModel>()
                        .Where(fn => fn.FolderPath == folderPath)
                        .DeleteAsync();
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

        /// <summary>
        /// データベースのクリーンアップを実行します
        /// </summary>
        /// <param name="progressCallback">進捗状況を報告するコールバック</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>クリーンアップされたエントリ数</returns>
        public async Task<(int deletedZeroRating, int deletedMissing)> CleanupDatabaseAsync(
            Action<string, double> progressCallback,
            CancellationToken cancellationToken)
        {
            var deletedZeroRating = 0;
            var deletedMissing = 0;
            await _dbAccess.WriteAsync(async db =>
            {
                // Rating = 0のエントリを削除
                progressCallback(App.Current.Resources["String_Settings_Developer_DeletingEntries"] as string, 0);
                deletedZeroRating = await db.GetTable<FileNodeModel>()
                    .Where(fn => fn.Rating == 0)
                    .DeleteAsync(cancellationToken);

                // LastCheckedTimeがNullまたは3日以前のエントリを確認
                progressCallback(App.Current.Resources["String_Settings_Developer_CheckingFiles"] as string, 0);
                var oldEntries = await db.GetTable<FileNodeModel>()
                    .Where(fn => fn.LastCheckedTime == null ||
                           fn.LastCheckedTime < DateTime.Now.AddDays(-3))
                    .ToListAsync(cancellationToken);

                int totalFiles = oldEntries.Count;
                int processedFiles = 0;

                foreach (var entry in oldEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    processedFiles++;

                    var progress = (double)processedFiles / totalFiles;
                    progressCallback(
                        string.Format(
                            App.Current.Resources["String_Settings_Developer_CheckingFilesProgress"] as string,
                            processedFiles,
                            totalFiles),
                        progress);

                    if (!File.Exists(entry.FullPath))
                    {
                        // ファイルが存在しない場合は削除
                        await db.GetTable<FileNodeModel>()
                            .Where(fn => fn.FullPath == entry.FullPath)
                            .DeleteAsync(cancellationToken);
                        deletedMissing++;
                    }
                    else
                    {
                        // ファイルが存在する場合はLastCheckedTimeを更新
                        entry.LastCheckedTime = DateTime.Now;
                        await db.UpdateAsync(entry);
                    }
                }

                progressCallback(App.Current.Resources["String_Settings_Developer_CleanupComplete"] as string, 1.0);

                // データベースの最適化を実行
                progressCallback(App.Current.Resources["String_Settings_Developer_OptimizingDatabase"] as string, 1.0);
                await db.ExecuteAsync("VACUUM;");
            }, cancellationToken);
            return (deletedZeroRating, deletedMissing);
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
}
