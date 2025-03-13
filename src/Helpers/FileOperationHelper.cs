using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Illustra.Models;

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
        public async Task MoveFile(string source, string dest)
        {
            ValidateFilePaths(source, dest);

            try
            {
                // レーティング情報の取得とファイル操作
                var sourceNode = await _db.GetFileNodeAsync(source);
                var rating = sourceNode?.Rating ?? 0;

                // ファイル移動
                File.Move(source, dest);

                // データベース更新
                await UpdateDatabaseAfterFileOperation(source, dest, rating, true);
            }
            catch (Exception ex)
            {
                throw new FileOperationException($"ファイルの移動中にエラーが発生しました: {source} から {dest} へ", ex);
            }
        }

        /// <summary>
        /// ファイルをコピーし、関連するデータベース情報も作成します
        /// </summary>
        public async Task CopyFile(string source, string dest)
        {
            ValidateFilePaths(source, dest);

            try
            {
                // レーティング情報の取得とファイル操作
                var sourceNode = await _db.GetFileNodeAsync(source);
                var rating = sourceNode?.Rating ?? 0;

                // ファイルコピー
                File.Copy(source, dest);

                // データベース更新（コピーの場合は元ファイルのノードは削除しない）
                await UpdateDatabaseAfterFileOperation(source, dest, rating, false);
            }
            catch (Exception ex)
            {
                throw new FileOperationException($"ファイルのコピー中にエラーが発生しました: {source} から {dest} へ", ex);
            }
        }

        /// <summary>
        /// ファイルを削除し、関連するデータベース情報も削除します
        /// </summary>
        public async Task DeleteFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            try
            {
                // ファイル削除
                File.Delete(path);

                // データベースから削除
                await _db.DeleteFileNodeAsync(path);
            }
            catch (Exception ex)
            {
                throw new FileOperationException($"ファイルの削除中にエラーが発生しました: {path}", ex);
            }
        }

        #region プライベートヘルパーメソッド

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
        private void ValidateFilePaths(string source, string dest)
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
    }

    public class FileOperationException : Exception
    {
        public FileOperationException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
