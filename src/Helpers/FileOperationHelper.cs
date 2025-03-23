using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Illustra.Models;
using Microsoft.VisualBasic.FileIO;
using XmpCore.Options;

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
        /// 複数のファイルに対してコピーまたは移動操作を実行します
        /// </summary>
        /// <param name="files">操作対象のファイルパスリスト</param>
        /// <param name="targetFolder">コピー/移動先のフォルダパス</param>
        /// <param name="isCopy">trueの場合はコピー、falseの場合は移動</param>
        /// <returns>操作の完了を表すTask</returns>
        public async Task<List<string>> ExecuteFileOperation(List<string> files, string targetFolder, bool isCopy)
        {
            if (files == null || files.Count == 0)
                throw new ArgumentException("ファイルリストが空です", nameof(files));

            if (string.IsNullOrEmpty(targetFolder))
                throw new ArgumentNullException(nameof(targetFolder));

            if (!Directory.Exists(targetFolder))
                throw new DirectoryNotFoundException($"ターゲットフォルダが見つかりません: {targetFolder}");

            var processedFiles = new List<string>();

            foreach (var file in files)
            {
                if (!File.Exists(file))
                    continue;

                string destPath = GetUniqueDestinationPath(file, targetFolder);

                try
                {
                    if (isCopy)
                    {
                        await CopyFile(file, destPath);
                    }
                    else
                    {
                        await MoveFile(file, destPath);
                    }
                    processedFiles.Add(destPath);
                }
                catch (Exception ex)
                {
                    throw new FileOperationException($"ファイル操作中にエラーが発生しました: {ex.Message}", ex);
                }
            }

            return processedFiles;
        }

        /// <summary>
        /// ファイルを移動し、関連するデータベース情報も更新します
        /// </summary>
        public async Task MoveFile(string srcPath, string destFolderPath, bool showUI = true, bool requireConfirmation = false)
        {
            ValidateFilePaths(srcPath, destFolderPath);

            try
            {
                // レーティング情報の取得とファイル操作
                var sourceNode = await _db.GetFileNodeAsync(srcPath);
                var rating = sourceNode?.Rating ?? 0;

                // ファイル移動
                // UIオプションの設定
                UIOption uiOption = showUI ? UIOption.AllDialogs : UIOption.OnlyErrorDialogs;

                // 上書きオプションの設定
                UICancelOption cancelOption = UICancelOption.DoNothing;

                // ファイルの移動
                FileSystem.MoveFile(
                    srcPath,
                    destFolderPath,
                    showUI: uiOption,
                    onUserCancel: cancelOption);

                // データベース更新
                await UpdateDatabaseAfterFileOperation(srcPath, destFolderPath, rating, true);
            }
            catch (Exception ex)
            {
                throw new FileOperationException($"ファイルの移動中にエラーが発生しました: {srcPath} から {destFolderPath} へ", ex);
            }
        }

        public async Task CopyFile(string srcPath, string destFolderPath, bool showUI = true, bool requireConfirmation = false)
        {
            ValidateFilePaths(srcPath, destFolderPath);

            try
            {
                // レーティング情報の取得とファイル操作
                var sourceNode = await _db.GetFileNodeAsync(srcPath);
                var rating = sourceNode?.Rating ?? 0;

                // UIオプションの設定
                UIOption uiOption = showUI ? UIOption.AllDialogs : UIOption.OnlyErrorDialogs;

                // 上書きオプションの設定
                UICancelOption cancelOption = UICancelOption.DoNothing;

                // ファイルのコピー
                FileSystem.CopyFile(
                    srcPath,
                    destFolderPath,
                    showUI: uiOption,
                    onUserCancel: cancelOption);

                // データベース更新（コピーの場合は元ファイルのノードは削除しない）
                await UpdateDatabaseAfterFileOperation(srcPath, destFolderPath, rating, false);
            }
            catch (Exception ex)
            {
                throw new FileOperationException($"ファイルのコピー中にエラーが発生しました: {srcPath} から {destFolderPath} へ", ex);
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
