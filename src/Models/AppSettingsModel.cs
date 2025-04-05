using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
// using Newtonsoft.Json; // 不要になった
using System.Text.Json.Serialization; // System.Text.Json の JsonConverter 属性を使うために追加
using Illustra.Helpers; // FavoriteFolderModelListConverter を使うために追加

namespace Illustra.Models
{
    public class AppSettingsModel
    {
        // ウィンドウサイズと位置
        public double WindowWidth { get; set; } = 900;
        public double WindowHeight { get; set; } = 600;
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public WindowState WindowState { get; set; } = WindowState.Normal;

        // 最後に開いていたフォルダ
        public string LastFolderPath { get; set; } = string.Empty;

        // サムネイルサイズ
        public int ThumbnailSize { get; set; } = 120;

        // 最後に選択したファイル
        public string LastSelectedFilePath { get; set; } = string.Empty;

        // スクロール設定
        public double MouseWheelMultiplier { get; set; } = 1.0;

        // ビューア設定
        public bool SaveViewerState { get; set; } = true;

        // ソート順設定
        public bool SortByDate { get; set; } = true;
        public bool SortAscending { get; set; } = true;

        // スプリッター位置設定
        public double FavoriteFoldersHeight { get; set; } = 0;
        public double MainSplitterPosition { get; set; } = 0;
        public double PropertySplitterPosition { get; set; } = 200;

        // メインウィンドウのプロパティパネル設定
        public bool MainPropertyPanelVisible { get; set; } = true;

        // お気に入りフォルダ
        // カスタムコンバーターを適用して旧設定ファイルとの互換性を保つ
        // カスタムコンバーターを適用 (System.Text.Json 用)
        [System.Text.Json.Serialization.JsonConverter(typeof(FavoriteFolderModelListConverter))]
        public ObservableCollection<FavoriteFolderModel> FavoriteFolders { get; set; } = new ObservableCollection<FavoriteFolderModel>();

        // アプリケーションの言語設定
        public string Language { get; set; } = CultureInfo.CurrentUICulture.Name;

        // プロパティパネルのフォルダパス折りたたみ状態
        public bool FolderPathExpanded { get; set; } = false;

        // プロパティパネルの詳細情報の折りたたみ状態
        public bool DetailsExpanded { get; set; } = false;

        // プロパティパネルのStable Diffusion情報の折りたたみ状態
        public bool StableDiffusionExpanded { get; set; } = false;

        // キーボードショートカット設定
        public string KeyboardShortcuts { get; set; } = string.Empty;

        // リストの循環移動設定
        public bool EnableCyclicNavigation { get; set; } = false;

        // 開発者モード設定
        public bool DeveloperMode { get; set; } = false;


        // MCP Host 有効化設定
        public bool EnableMcpHost { get; set; } = false;
        // 起動時フォルダ設定
        public enum StartupFolderMode
        {
            None,           // フォルダを開かない
            LastOpened,     // 前回開いたフォルダ
            Specified      // 指定したフォルダ
        }
        public StartupFolderMode StartupMode { get; set; } = StartupFolderMode.LastOpened;
        public string StartupFolderPath { get; set; } = string.Empty;

        /// <summary>
        /// ログカテゴリの有効/無効設定
        /// </summary>
        public Dictionary<string, bool> LogCategories { get; set; } = new Dictionary<string, bool>();

        // スタートアップ時に最後に開いていたファイルを選択
        public bool SelectLastFileOnStartup { get; set; } = false;

        // テーマ設定
        public string Theme { get; set; } = "Dark";

        // 新規ファイル追加時に自動選択するかどうか
        public bool AutoSelectNewFile { get; set; } = false;

        // タブの状態リスト
        public List<TabState> TabStates { get; set; } = new List<TabState>();

        // 最後にアクティブだったタブのインデックス
        public int LastActiveTabIndex { get; set; } = -1; // デフォルトは -1 (タブなし or 不明)

    }
}
