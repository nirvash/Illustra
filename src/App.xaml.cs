using System;
using System.Windows;
using Prism.Ioc;
using Prism.DryIoc;
using Illustra.Services;
using Illustra.Views;
using Illustra.ViewModels;
using Prism.Events;
using System.Globalization;
using System.Threading;
using Illustra.Helpers;
using LinqToDB;
using LinqToDB.DataProvider.SQLite;
using LinqToDB.Data;
using LinqToDB.DataProvider;

namespace Illustra
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : PrismApplication
    {
        private readonly DatabaseManager _db = new();
        public bool EnableCyclicNavigation { get; set; }

        public static App Instance => (App)Current;

        protected override Window CreateShell()
        {
            // 言語サービスを初期化
            var languageService = Container.Resolve<LanguageService>();

            // イベントアグリゲーターを取得
            var eventAggregator = Container.Resolve<IEventAggregator>();

            // 言語変更イベントを購読
            eventAggregator.GetEvent<LanguageChangedEvent>().Subscribe(() =>
            {
                // 言語が変更されたときにリソースを更新
                UpdateResourceDictionaries();
            });

            // 初期言語設定を適用
            UpdateResourceDictionaries();

            // DIコンテナからメインウィンドウを解決して返す
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // サービスの登録
            containerRegistry.RegisterSingleton<LanguageService>();
            containerRegistry.RegisterSingleton<IEventAggregator, EventAggregator>();

            // ビューの登録
            containerRegistry.Register<LanguageSettingsViewModel>();
            containerRegistry.Register<KeyboardShortcutSettingsViewModel>();

            // MainWindowViewModelの登録（IRegionManagerの依存関係を削除）
            containerRegistry.RegisterSingleton<ViewModels.MainWindowViewModel>();

            containerRegistry.RegisterSingleton<DatabaseManager>();
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();

            // キーボードショートカットハンドラーを初期化
            KeyboardShortcutHandler.Instance.ReloadShortcuts();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // データベースの初期化
            InitializeDatabase();

            // AppSettingsから循環移動の設定を読み込む
            var settings = SettingsHelper.GetSettings();
            EnableCyclicNavigation = settings.EnableCyclicNavigation;

            base.OnStartup(e);
        }


        public void UpdateResourceDictionaries()
        {
            // 現在のカルチャに基づいてリソースディクショナリを更新
            var currentCulture = Thread.CurrentThread.CurrentUICulture;

            // デバッグ情報
            System.Diagnostics.Debug.WriteLine($"現在の言語: {currentCulture.Name}, TwoLetterISOLanguageName: {currentCulture.TwoLetterISOLanguageName}");

            // リソースディクショナリをクリア
            Resources.MergedDictionaries.Clear();

            try
            {
                // 言語に応じたリソースを読み込む
                var resourceDictionary = new ResourceDictionary();

                if (currentCulture.TwoLetterISOLanguageName == "ja")
                {
                    // 日本語のリソースを読み込む
                    try
                    {
                        resourceDictionary.Source = new Uri("/Illustra;component/Resources/Strings.ja.xaml", UriKind.Relative);
                        System.Diagnostics.Debug.WriteLine("日本語リソースを読み込みました");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"日本語リソース読み込みエラー: {ex.Message}");
                        // 日本語リソースが読み込めない場合は英語リソースを試す
                        resourceDictionary.Source = new Uri("/Illustra;component/Resources/Strings.xaml", UriKind.Relative);
                    }
                }
                else
                {
                    // 英語のリソースを読み込む
                    resourceDictionary.Source = new Uri("/Illustra;component/Resources/Strings.xaml", UriKind.Relative);
                    System.Diagnostics.Debug.WriteLine("英語リソースを読み込みました");
                }

                // リソースディクショナリを追加
                Resources.MergedDictionaries.Add(resourceDictionary);

            }
            catch (Exception ex)
            {
                // リソース読み込みエラーの場合はログに記録
                System.Diagnostics.Debug.WriteLine($"リソース読み込みエラー: {ex.Message}");
                MessageBox.Show($"リソースの読み込みに失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void InitializeDatabase()
        {
            try
            {
                // System.Data.SQLite を使う場合はプロバイダー名を指定
                SQLiteTools.ResolveSQLite("System.Data.SQLite");

                // データベースのパスを取得（Applicationデータフォルダを使用）
                string dbPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Illustra",
                    "illustra.db");

                // フォルダが存在しない場合は作成
                string dbDirectory = System.IO.Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dbDirectory) && !System.IO.Directory.Exists(dbDirectory))
                {
                    System.IO.Directory.CreateDirectory(dbDirectory);
                }

                // System.Data.SQLite の接続文字列形式を正しく使用
                string connectionString = $"Data Source={dbPath};Version=3;";

                // linq2dbのSQLiteプロバイダを取得して接続
                var dataProvider = SQLiteTools.GetDataProvider("SQLite");

                using (var db = new DataConnection(dataProvider, connectionString))
                {
                    var tables = db.DataProvider.GetSchemaProvider().GetSchema(db).Tables;
                    foreach (var table in tables)
                    {
                        System.Diagnostics.Debug.WriteLine($"Table: {table.TableName}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"データベースの初期化エラー: {ex.Message}");
                MessageBox.Show($"データベースの初期化に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }
    }
}

