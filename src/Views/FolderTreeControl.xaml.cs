using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Illustra.Events;
using Prism.Events;
using Prism.Ioc;

namespace Illustra.Views
{
    public partial class FolderTreeControl : UserControl, IActiveAware
    {
        private IEventAggregator? _eventAggregator;
        private string _currentSelectedFilePath = string.Empty;
        private bool ignoreSelectedChangedOnce;
        private const string CONTROL_ID = "FolderTree";

        #region IActiveAware Implementation
        public bool IsActive { get; set; }
        public event EventHandler? IsActiveChanged;
        #endregion

        // xaml でインスタンス化するためのデフォルトコンストラクタ
        public FolderTreeControl()
        {
            InitializeComponent();
            Loaded += FolderTreeControl_Loaded;
        }

        private void FolderTreeControl_Loaded(object sender, RoutedEventArgs e)
        {
            // ContainerLocatorを使ってEventAggregatorを取得
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視

            // お気に入り関連イベントの設定
            _eventAggregator.GetEvent<AddToFavoritesEvent>().Subscribe(OnAddToFavorites);
            _eventAggregator.GetEvent<RemoveFromFavoritesEvent>().Subscribe(OnRemoveFromFavorites);
        }

        private void OnFolderSelected(FolderSelectedEventArgs args)
        {
            if (args.Path == _currentSelectedFilePath) return;
            if (ignoreSelectedChangedOnce)
            {
                ignoreSelectedChangedOnce = false;
                return;
            }

            ignoreSelectedChangedOnce = false;
            _currentSelectedFilePath = args.Path;
        }

        // お気に入りへの追加
        private void OnAddToFavorites(string path)
        {
            var favoriteFoldersControl = FindFavoriteFoldersControl();
            if (favoriteFoldersControl != null && !favoriteFoldersControl.FavoriteFolders.Contains(path))
            {
                favoriteFoldersControl.AddFavoriteFolder(path);
            }
        }

        // お気に入りからの削除
        private void OnRemoveFromFavorites(string path)
        {
            var favoriteFoldersControl = FindFavoriteFoldersControl();
            if (favoriteFoldersControl != null && favoriteFoldersControl.FavoriteFolders.Contains(path))
            {
                favoriteFoldersControl.RemoveFavoriteFolder(path);
            }
        }

        // FavoriteFoldersControlを検索するヘルパーメソッド
        private FavoriteFoldersControl? FindFavoriteFoldersControl()
        {
            var mainWindow = Window.GetWindow(this);
            if (mainWindow == null) return null;

            return mainWindow.FindName("FavoriteFolders") as FavoriteFoldersControl;
        }

        public void SaveAllData()
        {
            // 必要に応じて実装
        }
    }
}
