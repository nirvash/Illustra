using Prism.Events;

namespace Illustra.Events
{
    public class FolderSelectedEventArgs
    {
        public string Path { get; set; }
        public string SourceId { get; set; }

        public FolderSelectedEventArgs(string path, string sourceId)
        {
            Path = path;
            SourceId = sourceId;
        }
    }

    /// <summary>
    /// フォルダが選択されたときにトリガーされるイベント
    /// </summary>
    public class FolderSelectedEvent : PubSubEvent<FolderSelectedEventArgs> { }

    /// <summary>
    /// フォルダ内の先頭ファイルの選択を要求するイベント
    /// </summary>
    public class SelectFileRequestEvent : PubSubEvent<string> { }

    /// <summary>
    /// ファイルが選択されたときにトリガーされるイベント
    /// </summary>
    public class FileSelectedEvent : PubSubEvent<string> { }

    /// <summary>
    /// お気に入りに追加するイベント
    /// </summary>
    public class AddToFavoritesEvent : PubSubEvent<string> { }

    /// <summary>
    /// お気に入りから削除するイベント
    /// </summary>
    public class RemoveFromFavoritesEvent : PubSubEvent<string> { }

    /// <summary>
    /// レーティング変更イベントの引数
    /// </summary>
    public class RatingChangedEventArgs
    {
        public string FilePath { get; set; } = string.Empty;
        public int Rating { get; set; }
    }

    /// <summary>
    /// レーティングが変更されたときにトリガーされるイベント
    /// </summary>
    public class RatingChangedEvent : PubSubEvent<RatingChangedEventArgs> { }
    /// <summary>
    /// ファイル操作の進行状況イベントの引数
    /// </summary>
    public class FileOperationProgressEventArgs : EventArgs
    {
        public string FileName { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public double ProgressPercentage => (double)BytesTransferred / TotalBytes * 100;

        // 追加プロパティ
        public int CurrentFile { get; set; }
        public int TotalFiles { get; set; }
        public string OperationType { get; set; }

        public FileOperationProgressEventArgs(string fileName, long bytesTransferred, long totalBytes)
        {
            FileName = fileName;
            BytesTransferred = bytesTransferred;
            TotalBytes = totalBytes;
            OperationType = "Unknown";
            CurrentFile = 1;
            TotalFiles = 1;
        }

        public FileOperationProgressEventArgs(string fileName, long bytesTransferred, long totalBytes,
            int currentFile, int totalFiles, string operationType)
        {
            FileName = fileName;
            BytesTransferred = bytesTransferred;
            TotalBytes = totalBytes;
            CurrentFile = currentFile;
            TotalFiles = totalFiles;
            OperationType = operationType;
        }
    }

    /// <summary>
    /// フィルタ変更イベントの引数
    /// </summary>
    public class FilterChangedEventArgs
    {
        public bool IsPromptFilterEnabled { get; set; }
        public int RatingFilter { get; set; }
        public string SourceId { get; set; }

        public FilterChangedEventArgs(bool isPromptFilterEnabled, int ratingFilter, string sourceId)
        {
            IsPromptFilterEnabled = isPromptFilterEnabled;
            RatingFilter = ratingFilter;
            SourceId = sourceId;
        }
    }

    /// <summary>
    /// フィルタが変更されたときにトリガーされるイベント
    /// </summary>
    public class FilterChangedEvent : PubSubEvent<FilterChangedEventArgs> { }

    /// <summary>
    /// ファイル操作の進行状況イベント
    /// </summary>
    public class FileOperationProgressEvent : PubSubEvent<FileOperationProgressEventArgs> { }
}
