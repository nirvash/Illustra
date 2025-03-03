using System.Windows;
using Illustra.Views;
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
    public partial class App : Prism.DryIoc.PrismApplication
    {
        private readonly DatabaseManager _db = new();

        protected override Window CreateShell()
        {
            // DIコンテナからメインウィンドウを解決して返す
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<IEventAggregator, EventAggregator>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // データベースの初期化
            InitializeDatabase();
            base.OnStartup(e);
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

