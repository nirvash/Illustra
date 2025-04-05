﻿using Illustra.Shared.Models.Tools; // Added for McpOpenFolderEvent/Args
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
using System.Diagnostics;
using Illustra.Events;
using ControlzEx.Theming;
using System.IO;
using Illustra.Models;
using Illustra.Shared.Models; // Added for MCP events
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder; // Required for IApplicationBuilder extension methods

namespace Illustra
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : PrismApplication
    {
        private readonly DatabaseManager _db = new();
        public bool EnableCyclicNavigation { get; set; }

        // Web API ホストのインスタンスを保持
        private IHost? _mcpHost;

        // アプリケーション全体で共有するサービスへの参照を保持
        private IllustraAppContext _appContext;
        private IImagePropertiesService _propertiesService;

        public static App Instance => (App)Current;

        protected override Window CreateShell()
        {
            // 言語サービスを初期化
            var languageService = Container.Resolve<LanguageService>();

            // イベントアグリゲーターを取得
            var eventAggregator = Container.Resolve<IEventAggregator>();

            // IllustraAppContextを初期化（参照を保持）
            _appContext = Container.Resolve<IllustraAppContext>();

            // ImagePropertiesServiceを初期化（参照を保持）
            _propertiesService = Container.Resolve<IImagePropertiesService>();
            _propertiesService.Initialize();

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
            containerRegistry.RegisterSingleton<DatabaseManager>();

            // IllustraAppContextとImagePropertiesServiceの登録
            // アプリケーションライフサイクルと同じライフタイムを保証するためシングルトンとして登録
            // IllustraAppContext の登録時に DatabaseManager を注入
            containerRegistry.RegisterSingleton<IllustraAppContext>(resolver =>
                new IllustraAppContext(
                    resolver.Resolve<ThumbnailListViewModel>(), // MainViewModel も解決して渡す
                    resolver.Resolve<DatabaseManager>()
                ));
            containerRegistry.RegisterSingleton<IImagePropertiesService, ImagePropertiesService>();
            containerRegistry.RegisterSingleton<ThumbnailListViewModel>(); // MainViewModel をシングルトンで登録

            // ビューの登録
            containerRegistry.Register<LanguageSettingsViewModel>();
            containerRegistry.Register<KeyboardShortcutSettingsViewModel>();

            // MainWindowViewModelの登録（IRegionManagerの依存関係を削除）
            containerRegistry.RegisterSingleton<ViewModels.MainWindowViewModel>();
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();

            try
            {
                // メインウィンドウが閉じられたらアプリ終了
                Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;

                // キーボードショートカットハンドラーを初期化
                KeyboardShortcutHandler.Instance.ReloadShortcuts();

                LogHelper.LogWithTimestamp("アプリケーション初期化完了", LogHelper.Categories.UI);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("アプリケーション初期化エラー", ex);
            }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // データベースのデバッグログを設定
            var dbManager = Container.Resolve<DatabaseManager>();
            bool enableDebugLogging = false; // デバッグログを無効化
            dbManager.EnableDebugLogging(enableDebugLogging);

            if (enableDebugLogging)
            {
                Debug.WriteLine("[APP] データベースデバッグログを有効化しました");
            }
            else
            {
                Debug.WriteLine("[APP] データベースデバッグログを無効化しました");
            }

            // データベースの初期化
            InitializeDatabase();

            // 設定を読み込む
            var settings = SettingsHelper.GetSettings();

            // Resolve IEventAggregator early
            var eventAggregator = Container.Resolve<IEventAggregator>();
            // var dispatcherService = new WpfDispatcherService(); // Removed

            // MCP Host の起動制御
            if (settings.EnableMcpHost)
            {
                try
                {
                    // Web API ホストを起動（ポート5149を使用 - launchSettings.json に合わせる）
                    // IEventAggregator instance is already resolved

                    _mcpHost = Host.CreateDefaultBuilder()
                        .ConfigureServices((hostContext, services) => // Add ConfigureServices here
                        {
                            // Register the existing IEventAggregator instance as a singleton
                            services.AddSingleton(eventAggregator);
                            // Register the WPF Dispatcher instance as a singleton
                            services.AddSingleton(Application.Current.Dispatcher);
                        })
                        .ConfigureWebHostDefaults(webBuilder =>
                        {
                            // Use Startup class for configuration
                            webBuilder.UseStartup<Illustra.MCPHost.Startup>();
                            // Set the URL
                            webBuilder.UseUrls("http://localhost:5149");
                        })
                        .Build();

                    await _mcpHost.StartAsync();
                    LogHelper.LogWithTimestamp("MCP Web API ホストを起動しました (Port: 5149)", LogHelper.Categories.MCP);
                }
                catch (Exception ex)
                {
                    // MCPホストの起動に失敗しても、エラーログを記録してアプリケーションの起動は続行する
                    LogHelper.LogError("MCP ホストの起動中にエラーが発生しました", ex);
                    _mcpHost = null; // 念のためホスト参照をクリア
                }
            }
            else
            {
                LogHelper.LogWithTimestamp("MCP Web API ホストは無効化されています", LogHelper.Categories.MCP);
            }

            EnableCyclicNavigation = settings.EnableCyclicNavigation;

            // 保存されたテーマを適用
            ChangeTheme(settings.Theme);

            // ログカテゴリ設定を読み込む
            LogHelper.LoadCategorySettings();

            // var eventAggregator = Container.Resolve<IEventAggregator>(); // Already resolved earlier

            // コマンドライン引数からファイルパスを取得
            if (e.Args.Length > 0)
            {
                var filePath = e.Args[0];
                if (File.Exists(filePath))
                {
                    // ファイルが存在する場合、そのフォルダを開いて対象ファイルを選択
                    var folderPath = Path.GetDirectoryName(filePath);
                    eventAggregator.GetEvent<McpOpenFolderEvent>().Publish(
                        new McpOpenFolderEventArgs
                        {
                            FolderPath = folderPath!,
                            SourceId = "App",
                            SelectedFilePath = filePath,
                            ResultCompletionSource = null // No need to wait for result here
                        });
                }
                else
                {
                    // ファイルが存在しない場合は設定に基づいて動作
                    OpenFolderBasedOnSettings(settings, eventAggregator);
                }
            }
            else
            {
                // 引数がない場合は設定に基づいて動作
                OpenFolderBasedOnSettings(settings, eventAggregator);
            }
        }

        private void OpenFolderBasedOnSettings(AppSettingsModel settings, IEventAggregator eventAggregator)
        {
            string? folderToOpen = null;
            string? fileToSelect = null;

            switch (settings.StartupMode)
            {
                case AppSettingsModel.StartupFolderMode.LastOpened:
                    // タブ対応前の設定で開かれたときに最後に開いていたフォルダを指定
                    if (settings.TabStates == null || settings.TabStates.Count == 0)
                    {
                        folderToOpen = settings.LastFolderPath;
                    }
                    fileToSelect = settings.LastSelectedFilePath;
                    break;

                case AppSettingsModel.StartupFolderMode.Specified:
                    if (!string.IsNullOrEmpty(settings.StartupFolderPath) &&
                        Directory.Exists(settings.StartupFolderPath))
                    {
                        folderToOpen = settings.StartupFolderPath;
                    }
                    if (settings.SelectLastFileOnStartup &&
                        File.Exists(settings.LastSelectedFilePath))
                    {
                        fileToSelect = settings.LastSelectedFilePath;
                    }
                    break;
            }

            // フォルダが指定されている場合は開く
            if (!string.IsNullOrEmpty(folderToOpen) && Directory.Exists(folderToOpen))
            {
                eventAggregator.GetEvent<McpOpenFolderEvent>().Publish(
                    new McpOpenFolderEventArgs
                    {
                        FolderPath = folderToOpen,
                        SourceId = "App",
                        SelectedFilePath = fileToSelect,
                        ResultCompletionSource = null // No need to wait for result here
                    });
            }
        }

        public void ChangeTheme(string theme)
        {
            ResourceDictionary newTheme = new ResourceDictionary();
            switch (theme)
            {
                case "Dark":
                    newTheme.Source = new Uri("Themes/Dark.xaml", UriKind.Relative);
                    break;
                default:
                    newTheme.Source = new Uri("Themes/Light.xaml", UriKind.Relative);
                    break;
            }

            // 既存の辞書を削除せずに、新しいテーマを追加して入れ替える
            var oldTheme = Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Themes/"));
            if (oldTheme != null)
            {
                Resources.MergedDictionaries.Remove(oldTheme);
            }
            Resources.MergedDictionaries.Add(newTheme);

            // Sliderスタイルリソースを常に最後に追加して上書き
            var sliderStyleUri = new Uri("Themes/SliderStyles.xaml", UriKind.Relative);
            var existingSliderStyle = Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("SliderStyles.xaml"));

            if (existingSliderStyle != null)
            {
                Resources.MergedDictionaries.Remove(existingSliderStyle);
            }

            var sliderStyleDict = new ResourceDictionary { Source = sliderStyleUri };
            Resources.MergedDictionaries.Add(sliderStyleDict);
        }

        public void UpdateResourceDictionaries()
        {
            var currentCulture = Thread.CurrentThread.CurrentUICulture;
            System.Diagnostics.Debug.WriteLine($"現在の言語: {currentCulture.Name}, TwoLetterISOLanguageName: {currentCulture.TwoLetterISOLanguageName}");

            // `MahApps.Metro` のテーマを削除しないようにする
            var existingThemeDictionaries = Resources.MergedDictionaries
                .Where(d => d.Source != null && d.Source.OriginalString.Contains("MahApps.Metro"))
                .ToList();

            // 言語リソースのみ削除
            var oldLangDictionaries = Resources.MergedDictionaries
                .Where(d => d.Source != null && d.Source.OriginalString.Contains("Resources/Strings"))
                .ToList();

            foreach (var dict in oldLangDictionaries)
            {
                Resources.MergedDictionaries.Remove(dict);
            }

            try
            {
                // 言語リソースを読み込む
                var resourceDictionary = new ResourceDictionary();

                if (currentCulture.TwoLetterISOLanguageName == "ja")
                {
                    try
                    {
                        resourceDictionary.Source = new Uri("/Illustra;component/Resources/Strings.ja.xaml", UriKind.Relative);
                        System.Diagnostics.Debug.WriteLine("日本語リソースを読み込みました");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"日本語リソース読み込みエラー: {ex.Message}");
                        resourceDictionary.Source = new Uri("/Illustra;component/Resources/Strings.xaml", UriKind.Relative);
                    }
                }
                else
                {
                    resourceDictionary.Source = new Uri("/Illustra;component/Resources/Strings.xaml", UriKind.Relative);
                    System.Diagnostics.Debug.WriteLine("英語リソースを読み込みました");
                }

                // 言語リソースを追加（テーマリソースは維持）
                Resources.MergedDictionaries.Add(resourceDictionary);

                // `MahApps.Metro` のテーマを再適用（念のため）
                foreach (var dict in existingThemeDictionaries)
                {
                    if (!Resources.MergedDictionaries.Contains(dict))
                    {
                        Resources.MergedDictionaries.Add(dict);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"リソース読み込みエラー: {ex.Message}");
                MessageBox.Show(ex.ToString(), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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

        protected override void OnExit(ExitEventArgs e) // Changed to sync void
        {
            try
            {
                LogHelper.LogWithTimestamp("アプリケーション終了処理を開始", LogHelper.Categories.UI);

                // Web API ホストの停止
                if (_mcpHost != null)
                {
                    try
                    {
                        LogHelper.LogWithTimestamp("MCP Web API ホストの Dispose を開始します...", LogHelper.Categories.MCP);
                        _mcpHost.Dispose(); // StopAsync を呼ばずに直接 Dispose
                        LogHelper.LogWithTimestamp("MCP Web API ホストの Dispose が完了しました", LogHelper.Categories.MCP);
                    }
                    catch (Exception ex)
                    {
                        // Dispose中にエラーが発生した場合
                        LogHelper.LogError("MCP Web API ホストの Dispose 中にエラーが発生しました", ex);
                    }
                    finally
                    {
                        _mcpHost = null; // 参照をクリア
                        LogHelper.LogWithTimestamp("MCP Web API ホストの参照を null に設定しました", LogHelper.Categories.MCP);
                    }
                }
                else
                {
                    LogHelper.LogWithTimestamp("MCP Web API ホストは起動していませんでした", LogHelper.Categories.MCP);
                }

                // MainWindowViewModel の SaveTabStates を呼び出す
                try
                {
                    var mainWindowViewModel = Container.Resolve<MainWindowViewModel>();
                    mainWindowViewModel?.SaveTabStates();
                    LogHelper.LogWithTimestamp("タブ状態を保存しました", LogHelper.Categories.UI);
                }
                catch (Exception ex)
                {
                    LogHelper.LogError("タブ状態の保存中にエラーが発生しました", ex);
                }
                // イメージプロパティサービスのリソース解放
                if (_propertiesService is IDisposable disposableService)
                {
                    disposableService.Dispose();
                    LogHelper.LogWithTimestamp("ImagePropertiesServiceのリソースを解放", LogHelper.Categories.UI);
                }

                // データベース接続のクローズなど、その他のリソース解放
                // _db?.Dispose();

                LogHelper.LogWithTimestamp("アプリケーション終了処理完了", LogHelper.Categories.UI);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("アプリケーション終了処理エラー", ex);
            }
            finally
            {
                base.OnExit(e);
            }
        }
    }
}

