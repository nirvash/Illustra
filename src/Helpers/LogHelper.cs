using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Illustra.Helpers
{
    /// <summary>
    /// アプリケーション全体で使用するログ機能を提供するヘルパークラス
    /// </summary>
    public static class LogHelper
    {
        // ログカテゴリの定義
        public static class Categories
        {
            public const string Default = "DEFAULT";
            public const string Error = "ERROR";
            public const string Performance = "PERFORMANCE";
            public const string ThumbnailLoader = "THUMBNAIL_LOADER";
            public const string FileOperation = "FILE_OPERATION";
            public const string Database = "DATABASE";
            public const string UI = "UI";
            public const string Navigation = "NAVIGATION";

            // 解析用の特別なカテゴリ
            public const string Analysis = "ANALYSIS";
            public const string VisibilityDetection = "VISIBILITY_DETECTION";
            public const string ScrollTracking = "SCROLL_TRACKING";
        }

        // カテゴリごとの有効/無効状態を管理する辞書
        private static readonly Dictionary<string, bool> _enabledCategories = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // 静的コンストラクタでデフォルト設定を初期化
        static LogHelper()
        {
            // デフォルトですべてのカテゴリを有効化
            _enabledCategories[Categories.Default] = true;
            _enabledCategories[Categories.Error] = true;
            _enabledCategories[Categories.Performance] = true;
            _enabledCategories[Categories.ThumbnailLoader] = true;
            _enabledCategories[Categories.FileOperation] = true;
            _enabledCategories[Categories.Database] = true;
            _enabledCategories[Categories.UI] = true;
            _enabledCategories[Categories.Navigation] = true;
            _enabledCategories[Categories.Analysis] = true;
            _enabledCategories[Categories.VisibilityDetection] = true;
            _enabledCategories[Categories.ScrollTracking] = true;
        }

        /// <summary>
        /// 指定されたカテゴリのログ出力を有効または無効にします
        /// </summary>
        /// <param name="category">カテゴリ名</param>
        /// <param name="enabled">有効にする場合はtrue、無効にする場合はfalse</param>
        public static void SetCategoryEnabled(string category, bool enabled)
        {
            if (string.IsNullOrEmpty(category))
                return;

            _enabledCategories[category] = enabled;
        }

        /// <summary>
        /// 指定されたカテゴリのログ出力が有効かどうかを取得します
        /// </summary>
        /// <param name="category">カテゴリ名</param>
        /// <returns>有効な場合はtrue、無効な場合はfalse</returns>
        public static bool IsCategoryEnabled(string category)
        {
            if (string.IsNullOrEmpty(category))
                return true;

            return _enabledCategories.TryGetValue(category, out bool enabled) ? enabled : true;
        }

        /// <summary>
        /// すべてのカテゴリの有効/無効状態を設定から読み込みます
        /// </summary>
        public static void LoadCategorySettings()
        {
            try
            {
                var settings = SettingsHelper.GetSettings();

                // 開発者モードが有効な場合のみ設定を読み込む
                if (settings.DeveloperMode && settings.LogCategories != null && settings.LogCategories.Count > 0)
                {
                    foreach (var category in settings.LogCategories)
                    {
                        _enabledCategories[category.Key] = category.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] ログカテゴリ設定の読み込み中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在のカテゴリ設定を保存します
        /// </summary>
        public static void SaveCategorySettings()
        {
            try
            {
                var settings = SettingsHelper.GetSettings();

                // 開発者モードが有効な場合のみ設定を保存
                if (settings.DeveloperMode)
                {
                    settings.LogCategories = new Dictionary<string, bool>(_enabledCategories);
                    SettingsHelper.SaveSettings(settings);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] ログカテゴリ設定の保存中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// タイムスタンプ付きでログメッセージを出力します
        /// </summary>
        /// <param name="message">ログメッセージ</param>
        /// <param name="category">ログのカテゴリ（省略可）</param>
        /// <param name="callerName">呼び出し元のメソッド名（自動設定）</param>
        /// <param name="callerFilePath">呼び出し元のファイルパス（自動設定）</param>
        /// <param name="callerLineNumber">呼び出し元の行番号（自動設定）</param>
        public static void LogWithTimestamp(
            string message,
            string category = null,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            // カテゴリが無効な場合は出力しない
            if (!string.IsNullOrEmpty(category) && !IsCategoryEnabled(category))
                return;

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string caller = string.IsNullOrEmpty(category)
                ? $"{System.IO.Path.GetFileName(callerFilePath)}:{callerName}"
                : category;

            Debug.WriteLine($"[{timestamp}] [{caller}] {message}");
        }

        /// <summary>
        /// エラーメッセージをログに出力します
        /// </summary>
        /// <param name="message">エラーメッセージ</param>
        /// <param name="ex">例外オブジェクト（省略可）</param>
        /// <param name="callerName">呼び出し元のメソッド名（自動設定）</param>
        public static void LogError(
            string message,
            Exception ex = null,
            [CallerMemberName] string callerName = "")
        {
            string errorDetails = ex != null ? $" - 例外: {ex.Message}" : "";
            LogWithTimestamp($"[エラー] {message}{errorDetails}", Categories.Error, callerName);
        }

        /// <summary>
        /// 処理時間を計測してログに出力するためのストップウォッチを作成します
        /// </summary>
        /// <param name="operationName">計測する処理の名前</param>
        /// <param name="category">ログのカテゴリ（省略可）</param>
        /// <returns>計測用のストップウォッチオブジェクト</returns>
        public static Stopwatch StartTimeMeasurement(string operationName, string category = null)
        {
            var sw = new Stopwatch();
            sw.Start();
            LogWithTimestamp($"[開始] {operationName}", category ?? Categories.Performance);
            return sw;
        }

        /// <summary>
        /// 処理時間の計測を終了し、結果をログに出力します
        /// </summary>
        /// <param name="sw">計測用のストップウォッチ</param>
        /// <param name="operationName">計測した処理の名前</param>
        /// <param name="additionalInfo">追加情報（省略可）</param>
        /// <param name="category">ログのカテゴリ（省略可）</param>
        public static void EndTimeMeasurement(Stopwatch sw, string operationName, string additionalInfo = null, string category = null)
        {
            sw.Stop();
            string info = string.IsNullOrEmpty(additionalInfo) ? "" : $" - {additionalInfo}";
            LogWithTimestamp($"[完了] {operationName}: {sw.ElapsedMilliseconds}ms{info}", category ?? Categories.Performance);
        }

        /// <summary>
        /// パフォーマンス分析用のログを出力します
        /// </summary>
        /// <param name="message">ログメッセージ</param>
        public static void LogAnalysis(string message)
        {
            LogWithTimestamp(message, Categories.Analysis);
        }

        /// <summary>
        /// 可視性検出に関するログを出力します
        /// </summary>
        /// <param name="message">ログメッセージ</param>
        public static void LogVisibilityDetection(string message)
        {
            LogWithTimestamp(message, Categories.VisibilityDetection);
        }

        /// <summary>
        /// スクロール追跡に関するログを出力します
        /// </summary>
        /// <param name="message">ログメッセージ</param>
        public static void LogScrollTracking(string message)
        {
            LogWithTimestamp(message, Categories.ScrollTracking);
        }
    }
}