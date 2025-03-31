using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Illustra.Models; // FileOperationProgressInfo を使うために追加
using Microsoft.VisualBasic.FileIO;

namespace Illustra.Helpers
{
    public class FileOperationHelper
    {
        private readonly DatabaseManager _db;

        public FileOperationHelper(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// ファイル名を変更し、関連するデータベース情報も更新します
        /// </summary>
        public async Task<FileNodeModel> RenameFile(string oldPath, string newPath)
        {
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
                throw new ArgumentNullException("ファイルパスが無効です");

            if (!File.Exists(oldPath))
                throw new FileNotFoundException("元のファイルが見つかりません", oldPath);

            if (oldPath == newPath)
                return null; // 変更なし;

            try
            {
                // ファイル名を変更
                File.Move(oldPath, newPath);

                // データベースの更新
                return await _db.HandleFileRenamedAsync(oldPath, newPath);
            }
            catch (Exception ex)
            {
                throw new FileOperationException($"ファイル名の変更中にエラーが発生しました: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 複数のファイルに対してコピーまたは移動操作を実行します
        /// </summary>
        /// <param name="files">操作対象のファイルパスリスト</param>
        /// <param name="targetFolder">コピー/移動先のフォルダパス</param>
        /// <param name="isCopy">trueの場合はコピー、falseの場合は移動</param>
        /// <param name="progress">ファイル単位の進捗を報告するための IProgress インスタンス</param>
        /// <param name="postProcessAction">各ファイルの処理完了後に呼び出されるアクション</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>操作が成功したファイルのパスリスト</returns>
        public async Task<List<string>> ExecuteFileOperation(List<string> files, string targetFolder, bool isCopy, IProgress<FileOperationProgressInfo>? progress = null, Action<string>? postProcessAction = null, CancellationToken cancellationToken = default)
        {
            if (files == null || files.Count == 0)
                throw new ArgumentException("ファイルリストが空です", nameof(files));

            if (string.IsNullOrEmpty(targetFolder))
                throw new ArgumentNullException(nameof(targetFolder));

            if (!Directory.Exists(targetFolder))
                throw new DirectoryNotFoundException($"ターゲットフォルダが見つかりません: {targetFolder}");

            var processedFiles = new List<string>();
            int totalFiles = files.Count;
            int processedCount = 0;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested(); // Check for cancellation before processing each file
                processedCount++;
                string currentFileName = Path.GetFileName(file);
                // 処理開始前に進捗を報告 (処理済みファイル数はまだ前の値)
                progress?.Report(new FileOperationProgressInfo(currentFileName, totalFiles, processedCount - 1));

                if (!File.Exists(file))
                {
                    // ファイルが存在しない場合はスキップして進捗を更新
                    progress?.Report(new FileOperationProgressInfo(currentFileName, totalFiles, processedCount));
                    continue;
                }

                string destPath = GetUniqueDestinationPath(file, targetFolder);

                try
                {
                    // FileSystem を使うメソッドを呼び出す (showUI=false でダイアログ非表示)
                    if (isCopy)
                    {
                        // CopyFile は destFolderPath を期待するので修正 -> destPath を渡す
                        await CopyFile(file, destPath, showUI: false);
                    }
                    else
                    {
                        // MoveFile は destFolderPath を期待するので修正 -> destPath を渡す
                        await MoveFile(file, destPath, showUI: false);
                    }
                    processedFiles.Add(destPath);
                    // 処理完了後に進捗を更新
                    progress?.Report(new FileOperationProgressInfo(currentFileName, totalFiles, processedCount));
                    // ファイルごとの後処理を実行
                    postProcessAction?.Invoke(destPath);
                }
                catch (Exception ex)
                {
                    // エラーが発生した場合も進捗を進める
                    progress?.Report(new FileOperationProgressInfo(currentFileName, totalFiles, processedCount));
                    // エラー処理: ログ記録やユーザーへの通知など
                    LogHelper.LogError($"ファイル操作エラー ({(isCopy ? "コピー" : "移動")}): {file} -> {destPath} - {ex.Message}");
                    // 例外を再スローするか、処理を続行するかは要件による
                    // throw new FileOperationException($"ファイル操作中にエラーが発生しました: {ex.Message}", ex);
                }
            }

            // Ensure final progress report indicates completion
            progress?.Report(new FileOperationProgressInfo(string.Empty, totalFiles, totalFiles)); // Report completion
            return processedFiles;
        }

        /// <summary>
        /// ファイルを移動し、関連するデータベース情報も更新します
        /// </summary>
        /// <param name="srcPath">移動元のファイルパス</param>
        /// <param name="destPath">移動先のファイルパス（フォルダではない）</param>
        /// <param name="showUI">操作中にUIを表示するかどうか (ExecuteFileOperationからは常にfalse)</param>
        /// <param name="requireConfirmation">確認ダイアログを表示するかどうか (ExecuteFileOperationからは常にfalse)</param>
        public async Task MoveFile(string srcPath, string destPath, bool showUI = true, bool requireConfirmation = false)
        {
            ValidateFilePaths(srcPath, destPath); // destPath はファイルパス

            try
            {
                // レーティング情報の取得とファイル操作
                var sourceNode = await _db.GetFileNodeAsync(srcPath);
                var rating = sourceNode?.Rating ?? 0;

                // ファイル移動
                // UIオプションの設定
                Microsoft.VisualBasic.FileIO.UIOption uiOption = showUI ? Microsoft.VisualBasic.FileIO.UIOption.AllDialogs : Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs;

                // 上書きオプションの設定 (Copy/MoveFile は上書き確認しないため DoNothing)
                Microsoft.VisualBasic.FileIO.UICancelOption cancelOption = Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing;

                // ファイルの移動 (Microsoft.VisualBasic.FileIO を明示的に使用)
                Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(
                    srcPath,
                    destPath, // フォルダではなくファイルパスを渡す
                    showUI: uiOption,
                    onUserCancel: cancelOption);

                // データベース更新
                await UpdateDatabaseAfterFileOperation(srcPath, destPath, rating, true);
            }
            catch (Exception ex)
            {
                throw new FileOperationException($"ファイルの移動中にエラーが発生しました: {srcPath} から {destPath} へ", ex);
            }
        }

        /// <summary>
        /// ファイルをコピーし、関連するデータベース情報も更新します
        /// </summary>
        /// <param name="srcPath">コピー元のファイルパス</param>
        /// <param name="destPath">コピー先のファイルパス（フォルダではない）</param>
        /// <param name="showUI">操作中にUIを表示するかどうか (ExecuteFileOperationからは常にfalse)</param>
        /// <param name="requireConfirmation">確認ダイアログを表示するかどうか (ExecuteFileOperationからは常にfalse)</param>
        public async Task CopyFile(string srcPath, string destPath, bool showUI = true, bool requireConfirmation = false)
        {
            ValidateFilePaths(srcPath, destPath); // destPath はファイルパス

            try
            {
                // レーティング情報の取得とファイル操作
                var sourceNode = await _db.GetFileNodeAsync(srcPath);
                var rating = sourceNode?.Rating ?? 0;

                // UIオプションの設定
                Microsoft.VisualBasic.FileIO.UIOption uiOption = showUI ? Microsoft.VisualBasic.FileIO.UIOption.AllDialogs : Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs;

                // 上書きオプションの設定 (Copy/MoveFile は上書き確認しないため DoNothing)
                Microsoft.VisualBasic.FileIO.UICancelOption cancelOption = Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing;

                // ファイルのコピー (Microsoft.VisualBasic.FileIO を明示的に使用)
                Microsoft.VisualBasic.FileIO.FileSystem.CopyFile(
                    srcPath,
                    destPath, // フォルダではなくファイルパスを渡す
                    showUI: uiOption,
                    onUserCancel: cancelOption);

                // データベース更新（コピーの場合は元ファイルのノードは削除しない）
                await UpdateDatabaseAfterFileOperation(srcPath, destPath, rating, false);
            }
            catch (Exception ex)
            {
                throw new FileOperationException($"ファイルのコピー中にエラーが発生しました: {srcPath} から {destPath} へ", ex);
            }
        }

        /// <summary>
        /// ファイルを削除し、関連するデータベース情報も削除します
        /// </summary>
        /// <param name="path">削除するファイルのパス</param>
        /// <param name="useRecycleBin">trueの場合、ごみ箱に移動。falseの場合、完全削除</param>
        public async Task DeleteFile(string filePath, bool useRecycleBin, bool showUI = true, bool requireConfirmation = false)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            try
            {
                // UIオプションの設定
                UIOption uiOption = showUI ? UIOption.AllDialogs : UIOption.OnlyErrorDialogs;

                // リサイクルオプションの設定
                RecycleOption recycleOption = useRecycleBin ? RecycleOption.SendToRecycleBin : RecycleOption.DeletePermanently;

                // ファイルの削除
                FileSystem.DeleteFile(
                    filePath,
                    uiOption,
                    recycleOption
                );

                // データベースから削除 (ここで消さなくても Monitor で検知されて消える)
                await _db.DeleteFileNodeAsync(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルの削除中にエラーが発生しました: {filePath}\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                LogHelper.LogError($"ファイルの削除中にエラーが発生しました: {filePath}\n{ex.Message}");
                throw new FileOperationException($"ファイルの削除中にエラーが発生しました: {filePath}\n{ex.Message}", ex);
            }
        }

        /// <summary>
        /// フォルダを削除し、関連するデータベース情報も削除します
        /// </summary>
        /// <param name="directoryPath">削除するフォルダのパス</param>
        /// <param name="useRecycleBin">trueの場合、ごみ箱に移動。falseの場合、完全削除</param>
        /// <param name="showUI">操作中にUIを表示するかどうか</param>
        public async Task DeleteDirectoryAsync(string directoryPath, bool useRecycleBin, bool showUI = true)
        {
            if (string.IsNullOrEmpty(directoryPath))
                throw new ArgumentNullException(nameof(directoryPath));

            if (!Directory.Exists(directoryPath))
                throw new DirectoryNotFoundException($"フォルダが見つかりません: {directoryPath}");

            try
            {
                // UIオプションの設定
                UIOption uiOption = showUI ? UIOption.AllDialogs : UIOption.OnlyErrorDialogs;

                // リサイクルオプションの設定
                RecycleOption recycleOption = useRecycleBin ? RecycleOption.SendToRecycleBin : RecycleOption.DeletePermanently;

                // フォルダの削除 (STA スレッドで実行する必要がある場合がある)
                await RunOnStaThread(() =>
                {
                    FileSystem.DeleteDirectory(
                        directoryPath,
                        uiOption,
                        recycleOption
                    );
                });

                // データベースから関連フォルダ情報を削除 (必要に応じて実装)
                // FileSystemMonitor が検知して削除するため、ここでは不要な場合が多い
                // await _db.DeleteFolderNodeRecursiveAsync(directoryPath); // 例
            }
            catch (Exception ex)
            {
                // エラーメッセージをリソース化することも検討
                MessageBox.Show($"フォルダの削除中にエラーが発生しました: {directoryPath}\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                LogHelper.LogError($"フォルダの削除中にエラーが発生しました: {directoryPath}\n{ex.Message}");
                throw new FileOperationException($"フォルダの削除中にエラーが発生しました: {directoryPath}\n{ex.Message}", ex);
            }
        }


        #region プライベートヘルパーメソッド

        public static Task RunOnStaThread(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();

            Thread thread = new Thread(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return tcs.Task;
        }

        /// <summary>
        /// ソースパスとターゲットフォルダから一意のファイルパスを生成します
        /// </summary>
        private string GetUniqueDestinationPath(string sourcePath, string targetFolder)
        {
            string fileName = Path.GetFileName(sourcePath);
            string destPath = Path.Combine(targetFolder, fileName);

            // 同名ファイルが存在する場合は名前を変更
            if (File.Exists(destPath))
            {
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);
                int counter = 1;

                do
                {
                    destPath = Path.Combine(targetFolder, $"{fileNameWithoutExt} ({counter}){extension}");
                    counter++;
                } while (File.Exists(destPath));
            }

            return destPath;
        }

        /// <summary>
        /// ファイルパスのバリデーションを行います
        /// </summary>
        private static void ValidateFilePaths(string source, string dest)
        {
            if (string.IsNullOrEmpty(source))
                throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrEmpty(dest))
                throw new ArgumentNullException(nameof(dest));
        }

        /// <summary>
        /// ファイル操作後のデータベース更新を行います
        /// </summary>
        private async Task UpdateDatabaseAfterFileOperation(string source, string dest, int rating, bool isMove)
        {
            // 新しいノードを作成
            var newNode = new FileNodeModel(dest)
            {
                Rating = rating
            };
            await _db.SaveFileNodeAsync(newNode);

            // 移動の場合は古いノードを削除
            if (isMove)
            {
                await _db.DeleteFileNodeAsync(source);
            }
        }

        #endregion

        public class FileOperationException : Exception
        {
            public FileOperationException(string message, Exception? innerException = null)
                : base(message, innerException)
            {
            }
        }
    }
}
