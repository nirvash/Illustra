using Prism.Events;
using System.Windows.Input;
using System.Collections.Generic;
using Illustra.Models;
using Illustra.Helpers;

namespace Illustra.Events
{
    // FolderSelectedEvent and FolderSelectedEventArgs removed, replaced by McpOpenFolderEvent

    /// <summary>
    /// フォルダ内の先頭ファイルの選択を要求するイベント
    /// </summary>
    public class SelectFileRequestEvent : PubSubEvent<string> { }

    /// <summary>
    /// ファイルが選択されたときにトリガーされるイベント
    /// </summary>
    public class FileSelectedEvent : PubSubEvent<SelectedFileModel> { }

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

    public class LanguageChangedEvent : PubSubEvent { }

    /// <summary>
    /// フィルタ変更イベントの引数
    /// </summary>
    public class FilterChangedEventArgs
    {
        // 変更されたフィルタの種類を示す Enum
        public enum FilterChangedType
        {
            Prompt,
            Rating,
            Tag,
            Extension
        }

        /// <summary>
        /// 変更されたフィルタの種類を示すリスト。IsFullUpdate や IsClearOperation が false の場合に参照される。
        /// </summary>
        public List<FilterChangedType> ChangedTypes { get; } = new List<FilterChangedType>();

        /// <summary>
        /// このイベントがフィルタのクリア操作を示すかどうか。
        /// </summary>
        public bool IsClearOperation { get; set; } = false; // private set を削除

        /// <summary>
        /// このイベントがタブ切り替えなどによる全フィルタ設定の更新を示すかどうか。
        /// </summary>
        // IsFullUpdate フラグは不要になったため削除

        public bool IsPromptFilterEnabled { get; set; }
        public int RatingFilter { get; set; }
        public string SourceId { get; set; } = string.Empty;
        public bool IsTagFilterEnabled { get; set; }
        public List<string> TagFilters { get; set; } = new List<string>();
        public bool IsExtensionFilterEnabled { get; set; } // 追加
        public List<string> ExtensionFilters { get; set; } = new List<string>(); // 追加

        // パラメータなしのコンストラクタ
        public FilterChangedEventArgs(string sourceId)
        {
            SourceId = sourceId;
        }
    }

    public class FilterChangedEventArgsBuilder
    {
        public FilterChangedEventArgsBuilder(string sourceId)
        {
            _args = new FilterChangedEventArgs(sourceId);
            _args.SourceId = sourceId;
        }

        private FilterChangedEventArgs _args = null;

        public FilterChangedEventArgsBuilder WithPromptFilter(bool isEnabled)
        {
            _args.IsPromptFilterEnabled = isEnabled;
            if (!_args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Prompt))
                _args.ChangedTypes.Add(FilterChangedEventArgs.FilterChangedType.Prompt);
            _args.IsClearOperation = false; // 個別変更時はクリアではない
            // IsFullUpdate フラグは不要になったため削除
            return this;
        }

        public FilterChangedEventArgsBuilder WithRatingFilter(int rating)
        {
            _args.RatingFilter = rating;
            if (!_args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Rating))
                _args.ChangedTypes.Add(FilterChangedEventArgs.FilterChangedType.Rating);
            _args.IsClearOperation = false;
            // IsFullUpdate フラグは不要になったため削除
            return this;
        }

        public FilterChangedEventArgsBuilder WithTagFilter(bool isEnabled, List<string> tags)
        {
            _args.IsTagFilterEnabled = isEnabled;
            _args.TagFilters = tags ?? new List<string>(); // Null許容
            if (!_args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Tag))
                _args.ChangedTypes.Add(FilterChangedEventArgs.FilterChangedType.Tag);
            _args.IsClearOperation = false;
            // IsFullUpdate フラグは不要になったため削除
            return this;
        }

        public FilterChangedEventArgsBuilder WithExtensionFilter(bool isEnabled, List<string> extensions) // 追加
        {
            _args.IsExtensionFilterEnabled = isEnabled;
            _args.ExtensionFilters = extensions ?? new List<string>(); // nullチェック追加
            if (!_args.ChangedTypes.Contains(FilterChangedEventArgs.FilterChangedType.Extension))
                _args.ChangedTypes.Add(FilterChangedEventArgs.FilterChangedType.Extension);
            _args.IsClearOperation = false;
            // IsFullUpdate フラグは不要になったため削除
            return this;
        }

        /// <summary>
        /// フィルタのクリア操作を設定します。
        /// </summary>
        public FilterChangedEventArgsBuilder SetClear()
        {
            _args.IsClearOperation = true;
            // IsFullUpdate フラグは不要になったため削除
            _args.ChangedTypes.Clear();
            // クリア時の各フィルタ値はデフォルト値にリセット
            _args.RatingFilter = 0;
            _args.IsPromptFilterEnabled = false;
            _args.TagFilters.Clear();
            _args.IsTagFilterEnabled = false;
            _args.ExtensionFilters.Clear();
            _args.IsExtensionFilterEnabled = false;
            return this;
        }

        /// <summary>
        /// 全フィルタ設定の更新操作を設定します (タブ切り替え時など)。
        /// </summary>
        public FilterChangedEventArgsBuilder SetFullUpdate(FilterSettings settings)
        {
            // IsFullUpdate フラグは不要になったため削除
            _args.IsClearOperation = false;
            // 全更新の場合、すべての変更タイプが含まれているとみなす
            _args.ChangedTypes.AddRange(Enum.GetValues(typeof(FilterChangedEventArgs.FilterChangedType))
                                            .Cast<FilterChangedEventArgs.FilterChangedType>());
            // FilterSettings の値を引数にコピー
            _args.RatingFilter = settings.Rating;
            _args.IsPromptFilterEnabled = settings.HasPrompt;
            _args.IsTagFilterEnabled = settings.Tags?.Any() ?? false;
            _args.TagFilters = settings.Tags ?? new List<string>();
            _args.IsExtensionFilterEnabled = settings.Extensions?.Any() ?? false;
            _args.ExtensionFilters = settings.Extensions ?? new List<string>();
            return this;
        }

        public FilterChangedEventArgs Build()
        {
            return _args;
        }
    }

    /// <summary>
    /// フィルタが変更されたときにトリガーされるイベント
    /// </summary>
    public class FilterChangedEvent : PubSubEvent<FilterChangedEventArgs> { }

    /// <summary>
    /// 現在のフィルタ状態を取得するイベント
    /// </summary>
    public class GetFilterStateEvent : PubSubEvent<FilterChangedEventArgs> { }

    /// <summary>
    /// ファイル操作の進行状況イベント
    /// </summary>
    public class FileOperationProgressEvent : PubSubEvent<FileOperationProgressEventArgs> { }

    /// <summary>
    /// ツリービュー内の特定のアイテムを画面内に表示するためのイベント
    /// </summary>
    public class BringTreeItemIntoViewEvent : PubSubEvent<string> { }

    /// <summary>
    /// ショートカットキーイベント引数
    /// </summary>
    public class ShortcutKeyEventArgs
    {
        /// <summary>
        /// キー
        /// </summary>
        public Key Key { get; set; }

        /// <summary>
        /// 修飾キー（Ctrl, Shift, Alt）
        /// </summary>
        public ModifierKeys Modifiers { get; set; }

        /// <summary>
        /// イベント発生元のID
        /// </summary>
        public string? SourceId { get; set; }
    }

    /// <summary>
    /// ショートカットキーイベント
    /// </summary>
    public class ShortcutKeyEvent : PubSubEvent<ShortcutKeyEventArgs> { }

    /// <summary>
    /// ソート順変更イベントの引数
    /// </summary>
    public class SortOrderChangedEventArgs
    {
        public bool IsByDate { get; set; }
        public bool IsAscending { get; set; }
        public string SourceId { get; set; }

        public SortOrderChangedEventArgs(bool isByDate, bool isAscending, string sourceId)
        {
            IsByDate = isByDate;
            IsAscending = isAscending;
            SourceId = sourceId;
        }
    }

    /// <summary>
    /// ソート順が変更されたときにトリガーされるイベント
    /// </summary>
    public class SortOrderChangedEvent : PubSubEvent<SortOrderChangedEventArgs> { }


    // --- ViewModelからViewへのコマンド実行要求イベント ---
    public class RequestCopyEvent : PubSubEvent { }
    public class RequestPasteEvent : PubSubEvent { }
    public class RequestSelectAllEvent : PubSubEvent { }

    // ViewerSettingsが変更されたときのイベント
    public class ViewerSettingsChangedEvent : PubSubEvent { }
}
