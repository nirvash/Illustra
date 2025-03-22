using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Illustra.Events;
using Illustra.Helpers;

namespace Illustra.Models
{
    /// <summary>
    /// ファイルシステムのツリー構造を管理するモデル
    /// </summary>
    public class FileSystemTreeModel : INotifyPropertyChanged
    {
        private ObservableCollection<FileSystemItemModel> _rootItems = new ObservableCollection<FileSystemItemModel>();
        private FolderTreeSettings _folderTreeSettings;

        private bool _isLoading;
        public FileSystemTreeModel(FolderTreeSettings folderTreeSettings)
        {
            _rootItems = [];
            _folderTreeSettings = folderTreeSettings;
        }


        public void Dispose()
        {
            // すべてのルートアイテムの監視を停止
            foreach (var item in _rootItems)
            {
                item.Dispose();
            }

            _rootItems = new ObservableCollection<FileSystemItemModel>();
        }

        public ObservableCollection<FileSystemItemModel> RootItems
        {
            get => _rootItems;
            private set
            {
                if (_rootItems != value)
                {
                    _rootItems = value;
                    OnPropertyChanged(nameof(RootItems));
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged(nameof(IsLoading));
                }
            }
        }

        /// <summary>
        /// ツリー構造を初期化する
        /// </summary>
        /// <param name="initialPath">初期パス。nullの場合はドライブ一覧を表示</param>
        public void Initialize(string? initialPath = null)
        {
            IsLoading = true;
            RootItems.Clear();

            try
            {
                if (initialPath == null)
                {
                    // ドライブの列挙（準備中のドライブは除外）
                    var driveItems = GetDrives();

                    foreach (var item in driveItems)
                    {
                        RootItems.Add(item);
                    }
                }
                else
                {
                    // 特定のパスで初期化
                    ExpandPath(initialPath);

                    // フォルダを展開した後、そのノードを画面内に表示
                    var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                    eventAggregator.GetEvent<BringTreeItemIntoViewEvent>().Publish(initialPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初期化中にエラーが発生: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 指定したパスを展開する
        /// </summary>
        /// <param name="targetPath">展開対象のパス</param>
        public void ExpandPath(string? targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
            {
                Initialize(null);
                return;
            }

            IsLoading = true;

            try
            {
                string? rootPath = Path.GetPathRoot(targetPath);

                // ドライブ一覧が未取得の場合は取得
                if (RootItems.Count == 0)
                {
                    var drives = GetDrives();
                    foreach (var drive in drives)
                    {
                        RootItems.Add(drive);
                    }
                }

                // 該当するドライブを探して展開
                var rootDrive = RootItems.FirstOrDefault(
                    item => item.FullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase));

                if (rootDrive != null)
                {
                    // ドライブを展開状態に設定
                    rootDrive.IsExpanded = true;

                    // ドライブのパスよりも長い場合は階層的に展開
                    if (rootPath != null && targetPath.Length > rootPath.Length)
                    {
                        ExpandItemToPath(rootDrive, targetPath);
                    }
                    else
                    {
                        rootDrive.IsSelected = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"パス展開中にエラーが発生: {ex.Message}");
                MessageBox.Show($"パスの展開中にエラーが発生しました。\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 特定のアイテムから目的のパスまで展開する
        /// </summary>
        /// <param name="item">開始アイテム</param>
        /// <param name="targetPath">目的のパス</param>
        private void ExpandItemToPath(FileSystemItemModel item, string? targetPath)
        {
            if (item == null || !item.IsFolder) return;

            // このフォルダを展開してサブフォルダを読み込み
            item.IsExpanded = true;
            if (item.Children.Count == 1 && item.Children[0].IsDummy)
            {
                LoadSubFolders(item);
            }

            // このフォルダがターゲットと一致する場合は選択
            if (item.FullPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
            {
                item.IsSelected = true;
                return;
            }

            // 次の階層のフォルダを探す
            string? remainingPath = targetPath?.Substring(item.FullPath.Length).TrimStart(Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(remainingPath))
            {
                // このフォルダが目的地の場合
                item.IsSelected = true;
                return;
            }

            // 次の階層のフォルダ名を取得
            string nextFolderName = remainingPath.Split(Path.DirectorySeparatorChar)[0];
            string nextFolderPath = Path.Combine(item.FullPath, nextFolderName);

            // 次の階層のフォルダを探す
            var nextItem = item.Children.FirstOrDefault(
                child => child.FullPath.Equals(nextFolderPath, StringComparison.OrdinalIgnoreCase));

            if (nextItem != null)
            {
                // 次の階層に進む
                ExpandItemToPath(nextItem, targetPath);
            }
        }

        /// <summary>
        /// サブフォルダを読み込む
        /// </summary>
        /// <param name="parentItem">親フォルダ</param>
        public void LoadSubFolders(FileSystemItemModel? parentItem)
        {
            if (parentItem == null || !parentItem.IsFolder) return;

            try
            {
                parentItem.IsLoading = true;

                // パスが存在するか確認
                if (!Directory.Exists(parentItem.FullPath))
                {
                    Debug.WriteLine($"[フォルダツリー] パスが存在しません: {parentItem.FullPath}");
                    parentItem.CanExpand = false;
                    parentItem.Children.Clear();
                    parentItem.IsLoading = false;
                    return;
                }

                // ドライブパスの場合は、ドライブが準備できているか確認
                if (Path.GetPathRoot(parentItem.FullPath) == parentItem.FullPath)
                {
                    try
                    {
                        var driveInfo = new DriveInfo(parentItem.FullPath);
                        if (!driveInfo.IsReady)
                        {
                            Debug.WriteLine($"[フォルダツリー] ドライブが準備できていません: {parentItem.FullPath}");
                            parentItem.CanExpand = false;
                            parentItem.Children.Clear();
                            parentItem.IsLoading = false;
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[フォルダツリー] ドライブ情報取得エラー: {ex.Message}");
                        parentItem.CanExpand = false;
                        parentItem.Children.Clear();
                        parentItem.IsLoading = false;
                        return;
                    }
                }

                // 現在の子フォルダを取得
                var subFolders = GetSubFolders(parentItem.FullPath);

                // 展開可能状態を更新
                parentItem.CanExpand = subFolders.Count > 0;

                // 子フォルダを更新
                parentItem.Children.Clear();

                // 子フォルダがある場合は追加
                if (subFolders.Count > 0)
                {
                    foreach (var folder in subFolders)
                    {
                        parentItem.Children.Add(folder);
                        // 子フォルダの監視は、コンストラクタで自動的に開始される
                    }
                }
                // 子フォルダがなく、展開可能な場合はダミー要素を追加
                else if (parentItem.CanExpand)
                {
                    parentItem.Children.Add(new FileSystemItemModel("", true, true));
                }

                parentItem.IsDummy = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"サブフォルダ読み込み中にエラー: {ex.Message}");

                // エラー時は展開可能状態を更新
                try
                {
                    if (Directory.Exists(parentItem.FullPath))
                    {
                        var hasSubDirs = Directory.GetDirectories(parentItem.FullPath).Length > 0;
                        parentItem.CanExpand = hasSubDirs;

                        // 子フォルダをクリア
                        parentItem.Children.Clear();

                        // 展開可能な場合はダミー要素を追加
                        if (hasSubDirs)
                        {
                            parentItem.Children.Add(new FileSystemItemModel("", true, true));
                        }
                    }
                    else
                    {
                        parentItem.CanExpand = false;
                        parentItem.Children.Clear();
                    }
                }
                catch
                {
                    // 二重エラー時は何もしない
                    parentItem.CanExpand = false;
                    parentItem.Children.Clear();
                }
            }
            finally
            {
                parentItem.IsLoading = false;
            }
        }

        /// <summary>
        /// 指定したパスのサブフォルダを取得する
        /// </summary>
        /// <param name="path">親フォルダのパス</param>
        /// <returns>サブフォルダのリスト</returns>
        private List<FileSystemItemModel> GetSubFolders(string path)
        {
            var result = new List<FileSystemItemModel>();

            try
            {
                // ドライブパスの場合は、ドライブが準備できているか確認
                if (Path.GetPathRoot(path) == path)
                {
                    var driveInfo = new DriveInfo(path);
                    if (!driveInfo.IsReady)
                    {
                        // ドライブが準備できていない場合は空のリストを返す
                        Debug.WriteLine($"[フォルダツリー] ドライブが準備できていません: {path}");
                        return result;
                    }
                }

                var directories = Directory.GetDirectories(path)
                    .Where(dir =>
                    {
                        try
                        {
                            string dirName = Path.GetFileName(dir);
                            bool isHidden = (File.GetAttributes(dir) & FileAttributes.Hidden) == FileAttributes.Hidden;
                            return !isHidden && !dirName.StartsWith(".");
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .OrderBy(dir => dir)
                    .ToList();

                foreach (var dir in directories)
                {
                    bool hasSubDirectories = HasSubDirectories(dir);

                    var item = new FileSystemItemModel(dir, true, false, this)
                    {
                        CanExpand = hasSubDirectories
                    };

                    // 展開可能な場合はダミー要素を追加
                    if (hasSubDirectories)
                    {
                        item.Children.Add(new FileSystemItemModel("", true, true));
                    }

                    result.Add(item);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"サブフォルダ取得中にエラー: {ex.Message} (パス: {path})");
            }

            // ソート設定を取得
            var sortSettings = _folderTreeSettings.GetSortSettings(path);
            SortType sortType = sortSettings?.SortType ?? SortType.Name;
            bool ascending = sortSettings?.IsAscending ?? true;

            // ソート適用
            result = FolderSortHelper.Sort(result, sortType, ascending).ToList();

            return result;
        }

        /// <summary>
        /// ドライブ一覧を取得する
        /// </summary>
        /// <returns>ドライブ一覧</returns>
        private List<FileSystemItemModel> GetDrives()
        {
            var result = new List<FileSystemItemModel>();

            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(drive => drive.IsReady) // 準備中のドライブは除外
                    .OrderBy(drive => drive.Name);

                foreach (var drive in drives)
                {
                    bool hasSubDirectories = HasSubDirectories(drive.RootDirectory.FullName);

                    var driveItem = new FileSystemItemModel(drive.RootDirectory.FullName, true, false, this)
                    {
                        CanExpand = hasSubDirectories
                    };

                    // 展開可能状態に応じてダミー要素を管理
                    if (hasSubDirectories)
                    {
                        driveItem.Children.Add(new FileSystemItemModel("", true, true));
                    }

                    result.Add(driveItem);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ドライブ一覧取得中にエラー: {ex.Message}");
            }

            return result;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string? propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public FileSystemItemModel? FindItem(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            // ドライブ部分を取得
            string? rootPath = Path.GetPathRoot(path);
            if (rootPath == null)
                return null;

            // ルートアイテムを検索
            var rootItem = RootItems.FirstOrDefault(item =>
                item.FullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase));

            if (rootItem == null || rootPath == path)
                return rootItem;

            // サブフォルダを再帰的に検索
            return FindItemRecursive(rootItem, path);
        }

        private FileSystemItemModel? FindItemRecursive(FileSystemItemModel parent, string path)
        {
            foreach (var child in parent.Children)
            {
                if (child.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                    return child;

                if (path.StartsWith(child.FullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    // 子フォルダが未ロードの場合はロードする
                    if (child.Children.Count == 1 && child.Children[0].IsDummy)
                    {
                        LoadSubFolders(child);
                    }
                    return FindItemRecursive(child, path);
                }
            }
            return null;
        }

        /// <summary>
        /// ソート設定を更新し、フォルダの内容を再読み込みする
        /// </summary>
        /// <param name="path">対象フォルダのパス</param>
        /// <param name="sortType">ソートタイプ（nullの場合は既存の設定を維持）</param>
        /// <param name="ascending">昇順/降順（nullの場合は既存の設定を維持）</param>
        public void UpdateSort(string path, SortType? sortType = null, bool? ascending = null)
        {
            var settings = _folderTreeSettings.SortSettings.GetValueOrDefault(path);
            if (settings == null)
            {
                settings = new FolderSortSettings
                {
                    SortType = sortType ?? SortType.Name,
                    IsAscending = ascending ?? true
                };
                _folderTreeSettings.SortSettings[path] = settings;
            }
            else
            {
                if (sortType.HasValue) settings.SortType = sortType.Value;
                if (ascending.HasValue) settings.IsAscending = ascending.Value;
            }
            _folderTreeSettings.Save();

            // 対象フォルダを探してリロード
            var item = FindItem(path);
            if (item != null && item.IsExpanded)
            {
                LoadSubFolders(item);
            }
        }

        /// <summary>
        /// 指定されたフォルダのソート設定を取得
        /// </summary>
        public FolderSortSettings? GetSortSettings(string path)
        {
            return _folderTreeSettings.SortSettings.GetValueOrDefault(path);
        }

        /// <summary>
        /// すべてのノードの選択状態をクリアする
        /// </summary>
        public void ClearSelection()
        {
            void ClearRecursive(FileSystemItemModel item)
            {
                item.IsSelected = false;
                foreach (var child in item.Children)
                {
                    ClearRecursive(child);
                }
            }

            foreach (var item in RootItems)
            {
                ClearRecursive(item);
            }
        }
        /// <summary>
        /// 指定されたパスのフォルダに対するFileSystemItemModelを作成する
        /// </summary>
        /// <param name="path">フォルダのパス</param>
        /// <returns>作成されたモデル、またはnull（アクセス不可の場合）</returns>
        public FileSystemItemModel? CreateFolderItem(string path)
        {
            try
            {
                // アクセス可能で非表示でないことを確認
                if (!IsAccessibleFolder(path)) return null;

                // サブフォルダの有無を確認
                bool hasSubDirectories = HasSubDirectories(path);

                // 新しいフォルダアイテムを作成
                var item = new FileSystemItemModel(path, true, false, this)
                {
                    CanExpand = hasSubDirectories
                };

                // 展開可能な場合はダミー要素を追加
                if (hasSubDirectories)
                {
                    item.Children.Add(new FileSystemItemModel("", true, true));
                }

                return item;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[フォルダツリー] フォルダアイテム作成エラー: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// 指定したパスのフォルダにサブフォルダが存在するか確認する
        /// </summary>
        /// <param name="path">確認するフォルダのパス</param>
        /// <returns>サブフォルダが存在する場合はtrue</returns>
        public bool HasSubDirectories(string path)
        {
            try
            {
                return Directory.GetDirectories(path)
                    .Where(dir =>
                    {
                        try
                        {
                            string dirName = Path.GetFileName(dir);
                            bool isHidden = (File.GetAttributes(dir) & FileAttributes.Hidden) == FileAttributes.Hidden;
                            return !isHidden && !dirName.StartsWith(".");
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 指定したパスのフォルダにアクセス可能かつ非表示でないか確認する
        /// </summary>
        public bool IsAccessibleFolder(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.Hidden) != FileAttributes.Hidden
                    && !Path.GetFileName(path).StartsWith(".");
            }
            catch
            {
                return false;
            }
        }
    }
}
