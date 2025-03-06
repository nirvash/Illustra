using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Illustra.Helpers
{
    public class FileOperationHelper
    {
        /// <summary>
        /// ファイル操作の進捗状況を通知するイベント
        /// </summary>
        public event EventHandler<FileOperationProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// ファイル操作を実行します
        /// </summary>
        public async Task ExecuteFileOperation(IList<string> sourcePaths, string targetPath, bool isCopy)
        {
            try
            {
                // 操作前の検証
                ValidateOperation(sourcePaths, targetPath);

                int total = sourcePaths.Count;
                int current = 0;

                foreach (var sourcePath in sourcePaths)
                {
                    current++;
                    string fileName = Path.GetFileName(sourcePath);
                    string destinationPath = Path.Combine(targetPath, fileName);

                    // 同名ファイルの処理
                    destinationPath = GetUniqueFilePath(destinationPath);

                    try
                    {
                        if (isCopy)
                        {
                            await Task.Run(() => File.Copy(sourcePath, destinationPath, true));
                        }
                        else
                        {
                            await Task.Run(() => File.Move(sourcePath, destinationPath));
                        }

                        // 進捗を通知
                        OnProgressChanged(new FileOperationProgressEventArgs(
                            sourcePath,
                            destinationPath,
                            current,
                            total,
                            isCopy ? FileOperationType.Copy : FileOperationType.Move));
                    }
                    catch (Exception ex)
                    {
                        // 個別のファイル操作のエラーを通知
                        MessageBox.Show(
                            $"ファイル {fileName} の{(isCopy ? "コピー" : "移動")}中にエラーが発生しました：\n{ex.Message}",
                            "エラー",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                // 全体的なエラーを通知
                MessageBox.Show(
                    $"ファイル操作中にエラーが発生しました：\n{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                throw;
            }
        }

        /// <summary>
        /// ファイル操作の検証を行います
        /// </summary>
        private void ValidateOperation(IList<string> sourcePaths, string targetPath)
        {
            if (sourcePaths == null || sourcePaths.Count == 0)
                throw new ArgumentException("ソースファイルが指定されていません。");

            if (string.IsNullOrEmpty(targetPath))
                throw new ArgumentException("移動先が指定されていません。");

            if (!Directory.Exists(targetPath))
                throw new DirectoryNotFoundException("指定された移動先フォルダが見つかりません。");

            foreach (var path in sourcePaths)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"ファイル {Path.GetFileName(path)} が見つかりません。");
            }
        }

        /// <summary>
        /// 重複しないファイルパスを取得します
        /// </summary>
        private string GetUniqueFilePath(string originalPath)
        {
            if (!File.Exists(originalPath))
                return originalPath;

            string directory = Path.GetDirectoryName(originalPath) ?? "";
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);
            int counter = 1;

            string newPath;
            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExt} ({counter}){extension}");
                counter++;
            } while (File.Exists(newPath));

            return newPath;
        }

        /// <summary>
        /// 進捗状況の変更を通知します
        /// </summary>
        private void OnProgressChanged(FileOperationProgressEventArgs e)
        {
            ProgressChanged?.Invoke(this, e);
        }
    }

    public class FileOperationProgressEventArgs : EventArgs
    {
        public string SourcePath { get; }
        public string DestinationPath { get; }
        public int CurrentFile { get; }
        public int TotalFiles { get; }
        public FileOperationType OperationType { get; }

        public FileOperationProgressEventArgs(
            string sourcePath,
            string destinationPath,
            int currentFile,
            int totalFiles,
            FileOperationType operationType)
        {
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
            CurrentFile = currentFile;
            TotalFiles = totalFiles;
            OperationType = operationType;
        }
    }

    public enum FileOperationType
    {
        Copy,
        Move
    }
}
