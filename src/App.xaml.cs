using System.Windows;
using Prism.Ioc;
using Illustra.Views;
using Illustra.Events;
using Prism.Events;
using Illustra.Helpers;
using Prism.DryIoc;
using DryIoc;

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
            // データベースは自動的に初期化されます（DatabaseManagerのコンストラクタで）
            // 必要に応じて追加の初期化処理をここに記述できます
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }
    }
}

