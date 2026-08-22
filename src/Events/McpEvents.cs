using Prism.Events;

namespace Illustra.Events
{
    /// <summary>
    /// MCP ツール実行リクエストの基底イベント引数。
    /// UI 側ハンドラが処理完了時に <see cref="ResultCompletionSource"/> へ結果を設定する。
    /// </summary>
    public abstract class McpBaseEventArgs : EventArgs
    {
        /// <summary>発行者識別子。自己発火ループ防止のフィルタに使用。</summary>
        public string? SourceId { get; set; }

        /// <summary>MCP ツール側への結果返却用。</summary>
        public TaskCompletionSource<object>? ResultCompletionSource { get; set; }
    }

    /// <summary>
    /// フォルダを開くことを要求するイベント（UI 操作・MCP ツール共通）。
    /// </summary>
    public class McpOpenFolderEvent : PubSubEvent<McpOpenFolderEventArgs> { }

    public class McpOpenFolderEventArgs : McpBaseEventArgs
    {
        public string? FolderPath { get; set; }
        public string? SelectedFilePath { get; set; } // Optional file to select after opening
    }

    /// <summary>
    /// アプリケーションの正常終了を要求するイベント（MCP shutdown_application 用）。
    /// </summary>
    public class McpShutdownEvent : PubSubEvent<McpShutdownEventArgs> { }

    public class McpShutdownEventArgs : McpBaseEventArgs
    {
    }

    /// <summary>
    /// ファイルを選択することを要求するイベント（MCP select_file 用）。
    /// </summary>
    public class McpSelectFilesEvent : PubSubEvent<McpSelectFilesEventArgs> { }

    public class McpSelectFilesEventArgs : McpBaseEventArgs
    {
        public IReadOnlyList<string> Paths { get; set; } = [];
    }

    /// <summary>
    /// アクティブタブのファイル一覧取得を要求するイベント（MCP list_files 用）。
    /// </summary>
    public class McpGetFileListEvent : PubSubEvent<McpGetFileListEventArgs> { }

    public class FileListItemModel
    {
        public string Path { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime LastModified { get; set; }
        public int Rating { get; set; }
    }

    public class McpGetFileListEventArgs : McpBaseEventArgs
    {
        public string? FolderPath { get; set; }
        public List<FileListItemModel>? Files { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 選択中ファイル一覧の取得を要求するイベント（MCP get_selected_files 用）。
    /// </summary>
    public class McpGetSelectedFilesEvent : PubSubEvent<McpGetSelectedFilesEventArgs> { }

    public class SelectedFileInfoModel
    {
        public string Path { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }

    public class McpGetSelectedFilesEventArgs : McpBaseEventArgs
    {
        public List<SelectedFileInfoModel>? Files { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// アプリケーションの現在状態（アクティブフォルダ・選択中ファイル等）の取得を要求するイベント（MCP get_app_status 用）。
    /// </summary>
    public class McpGetAppStatusEvent : PubSubEvent<McpGetAppStatusEventArgs> { }

    public class McpGetAppStatusEventArgs : McpBaseEventArgs
    {
        public string? CurrentFolder { get; set; }
        public int LoadedFileCount { get; set; }
        public List<SelectedFileInfoModel>? SelectedFiles { get; set; }
        public List<string>? OpenTabs { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// MCP サーバー（Kestrel ホスト）の稼働状態が変化したことを通知するイベント。
    /// </summary>
    public class McpServerStatusChangedEvent : PubSubEvent<McpServerStatusChangedEventArgs> { }

    public class McpServerStatusChangedEventArgs
    {
        public bool IsRunning { get; set; }
        public string? EndpointUrl { get; set; }
    }
}
