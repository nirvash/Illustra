using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using Illustra.Helpers;
using System.IO;
using Illustra.Events;
using Prism.Events;

namespace Illustra.Views
{
    public partial class FolderTreeControl : UserControl
    {
        private IEventAggregator? _eventAggregator;
        private string _currentSelectedFilePath = string.Empty;
        private bool isFolderSelecting;
        private bool ignoreSelectedChangedOnce;

        #region IActiveAware Implementation
#pragma warning disable 0067 // 使用されていませんという警告を無視
        public bool IsActive { get; set; }
        public event EventHandler? IsActiveChanged;
#pragma warning restore 0067 // 警告の無視を終了
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
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected);
            LoadDrivesAsync();
        }

        private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (isFolderSelecting) return;
            if (e.NewValue is TreeViewItem { Tag: string path } selectedItem && path != null)
            {
                if (Directory.Exists(path))
                {
                    // フォルダ選択イベントを発行
                    ignoreSelectedChangedOnce = true;
                    _eventAggregator.GetEvent<FolderSelectedEvent>().Publish(path);
                    _eventAggregator.GetEvent<SelectFolderFirstItemRequestEvent>().Publish();
                    isFolderSelecting = false;
                }
            }
        }

        private void AddToFavorites_Click(object sender, RoutedEventArgs e)
        {
            if (FolderTreeView.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string path)
            {
                var favoriteFoldersControl = (FavoriteFoldersControl)FindName("FavoriteFoldersControl");
                if (favoriteFoldersControl != null && !favoriteFoldersControl.FavoriteFolders.Contains(path))
                {
                    favoriteFoldersControl.AddFavoriteFolder(path);
                }
            }
        }

        private void RemoveFromFavorites_Click(object sender, RoutedEventArgs e)
        {
            var favoriteFoldersControl = (FavoriteFoldersControl)FindName("FavoriteFoldersControl");
            if (favoriteFoldersControl != null && favoriteFoldersControl.FavoriteFoldersTreeView.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string path)
            {
                if (favoriteFoldersControl.FavoriteFolders.Contains(path))
                {
                    favoriteFoldersControl.RemoveFavoriteFolder(path);
                }
            }
        }

        private async void OnFolderSelected(string path)
        {
            if (path == _currentSelectedFilePath) return;
            if (ignoreSelectedChangedOnce)
            {
                ignoreSelectedChangedOnce = false;
                return;
            }
            ignoreSelectedChangedOnce = false;

            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                // フォルダツリーで選択されたフォルダを開く
                await SelectPathInTreeViewAsync(FolderTreeView, path);
            }
        }


        /// <summary>
        /// 指定されたパスに対応するTreeViewItemを見つけて選択状態にします
        /// </summary>
        /// <param name="treeView">ターゲットとなるTreeView</param>
        /// <param name="fullPath">選択したいフォルダの完全パス</param>
        /// <returns>パスが見つかって選択できた場合はtrue、それ以外はfalse</returns>
        public async Task<bool> SelectPathInTreeViewAsync(TreeView treeView, string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath) || treeView == null || treeView.Items.Count == 0)
                return false;

            try
            {
                // パスの各部分を取得
                var pathParts = FileSystemHelper.GetPathParts(fullPath);
                if (pathParts.Count == 0) return false;

                // ルート（ドライブ）を探す
                string rootPart = pathParts[0];
                TreeViewItem? rootNode = null;

                foreach (var item in treeView.Items)
                {
                    if (item is TreeViewItem tvi && tvi.Tag is string tag &&
                        tag.StartsWith(rootPart, StringComparison.OrdinalIgnoreCase))
                    {
                        rootNode = tvi;
                        break;
                    }
                }

                if (rootNode == null) return false;

                // ルートノードを展開
                rootNode.IsExpanded = true;

                // UIスレッドの更新を待機
                await Task.Delay(150);

                // 残りのパスを再帰的に探索
                TreeViewItem currentNode = rootNode;

                // 最初のパーツはドライブなので、それ以降を処理
                for (int i = 1; i < pathParts.Count; i++)
                {
                    string part = pathParts[i];
                    bool found = false;

                    // 子ノードをロードするために展開を確実に
                    if (!currentNode.IsExpanded)
                    {
                        currentNode.IsExpanded = true;
                        // 子ノードが非同期的にロードされる場合があるので少し待つ
                        await Task.Delay(150);
                    }

                    foreach (var item in currentNode.Items)
                    {
                        if (item is TreeViewItem childNode && childNode.Tag is string childPath)
                        {
                            string folderName = Path.GetFileName(childPath);
                            if (string.Equals(folderName, part, StringComparison.OrdinalIgnoreCase))
                            {
                                currentNode = childNode;
                                found = true;
                                break;
                            }
                        }
                    }

                    if (!found) return false;

                    // 最後のノード以外は展開する
                    if (i < pathParts.Count - 1)
                    {
                        currentNode.IsExpanded = true;
                        await Task.Delay(150); // UIの更新を待つ
                    }
                }

                // 最終ノードを選択状態にする - より効果的な選択を確保
                currentNode.IsSelected = true;
                currentNode.Focus(); // フォーカスを与える
                _currentSelectedFilePath = fullPath;


                // スクロールして見えるようにする
                currentNode.BringIntoView();

                // UIスレッドの処理を確実にするため少し待つ
                await Task.Delay(50);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"パスの選択中にエラーが発生: {ex.Message}");
                return false;
            }
        }

        private async void LoadDrivesAsync()
        {
            var drives = await FileSystemHelper.LoadDrivesAsync();
            foreach (var drive in drives)
            {
                await Dispatcher.InvokeAsync(() => FolderTreeView.Items.Add(FileSystemHelper.CreateDriveNode(drive)));
            }
        }
    }
}
