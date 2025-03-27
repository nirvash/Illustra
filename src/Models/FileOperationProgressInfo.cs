using System;

namespace Illustra.Models
{
    /// <summary>
    /// ファイル操作の進捗情報を表します。
    /// </summary>
    public class FileOperationProgressInfo
    {
        /// <summary>
        /// 現在処理中のファイル名。
        /// </summary>
        public string CurrentFileName { get; }

        /// <summary>
        /// 操作対象の総ファイル数。
        /// </summary>
        public int TotalFiles { get; }

        /// <summary>
        /// 処理済みのファイル数。
        /// </summary>
        public int ProcessedFiles { get; }

        /// <summary>
        /// 進捗率 (0.0 ～ 100.0)。
        /// </summary>
        public double ProgressPercentage { get; }

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="currentFileName">現在処理中のファイル名。</param>
        /// <param name="totalFiles">総ファイル数。</param>
        /// <param name="processedFiles">処理済みファイル数。</param>
        public FileOperationProgressInfo(string currentFileName, int totalFiles, int processedFiles)
        {
            CurrentFileName = currentFileName ?? string.Empty;
            TotalFiles = totalFiles;
            ProcessedFiles = processedFiles;
            ProgressPercentage = totalFiles > 0 ? (double)processedFiles / totalFiles * 100.0 : 0.0;
        }
    }
}
