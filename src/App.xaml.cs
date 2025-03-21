﻿using System;
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

            // IllustraAppContextを初期化
            var appContext = Container.Resolve<IllustraAppContext>();

            // ImagePropertiesServiceを初期化
            var propertiesService = Container.Resolve<IImagePropertiesService>();
            propertiesService.Initialize();

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

            // 新規追加: IllustraAppContextとImagePropertiesServiceの登録
            containerRegistry.RegisterSingleton<IllustraAppContext>();
            containerRegistry.RegisterSingleton<IImagePropertiesService, ImagePropertiesService>();

            // ビューの登録
            containerRegistry.Register<LanguageSettingsViewModel>();
            containerRegistry.Register<KeyboardShortcutSettingsViewModel>();

            // MainWindowViewModelの登録（IRegionManagerの依存関係を削除）
            containerRegistry.RegisterSingleton<ViewModels.MainWindowViewModel>();
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();

            // メインウィンドウが閉じられたらアプリ終了
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // キーボードショートカットハンドラーを初期化
            KeyboardShortcutHandler.Instance.ReloadShortcuts();
        }

        protected override void OnStartup(StartupEventArgs e)
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
            EnableCyclicNavigation = settings.EnableCyclicNavigation;

            // 保存されたテーマを適用
            ChangeTheme(settings.Theme);

            // ログカテゴリ設定を読み込む
            LogHelper.LoadCategorySettings();

            var eventAggregator = Container.Resolve<IEventAggregator>();

            // コマンドライン引数からファイルパスを取得
            if (e.Args.Length > 0)
            {
                var filePath = e.Args[0];
                if (File.Exists(filePath))
                {
                    // ファイルが存在する場合、そのフォルダを開いて対象ファイルを選択
                    var folderPath = Path.GetDirectoryName(filePath);
                    eventAggregator.GetEvent<FolderSelectedEvent>().Publish(
                        new FolderSelectedEventArgs(folderPath!, "App", filePath));
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
                    folderToOpen = settings.LastFolderPath;
                    fileToSelect = settings.LastSelectedFilePath;
                    break;

                case AppSettingsModel.StartupFolderMode.Specified:
                    if (!string.IsNullOrEmpty(settings.StartupFolderPath) &&
                        Directory.Exists(settings.StartupFolderPath))
                    {
                        folderToOpen = settings.StartupFolderPath;
                    }
                    break;
            }

            // フォルダが指定されている場合は開く
            if (!string.IsNullOrEmpty(folderToOpen) && Directory.Exists(folderToOpen))
            {
                eventAggregator.GetEvent<FolderSelectedEvent>().Publish(
                    new FolderSelectedEventArgs(folderToOpen, "App", fileToSelect));
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

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }
    }
}

