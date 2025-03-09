using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Illustra.Helpers;

namespace Illustra.Models
{
    /// <summary>
    /// ファイルシステムのツリー構造を管理するモデル
    /// </summary>
    public class FileSystemTreeModel : INotifyPropertyChanged, IFileSystemChangeHandler
    {
        private ObservableCollection<FileSystemItemModel> _rootItems;
        private bool _isLoading;
        private readonly Dictionary<string, FileSystemMonitor> _monitors;
        public FileSystemTreeModel(string? initialPath = null)
        {
            _rootItems = new ObservableCollection<FileSystemItemModel>();
            _monitors = new Dictionary<string, FileSystemMonitor>();
        }

        public void OnFileCreated(string path)
        {
            if (Directory.Exists(path))
            {
                Debug.WriteLine($"[フォルダツリー] フォルダ作成: {path}");
                // 各ルートアイテムにイベントを伝播
                foreach (var item in RootItems)
                {
                    if (item.OnFolderCreated(path))
                    {
                        break;  // いずれかのアイテムで処理された
                    }
                }
            }
        }

        public void OnFileDeleted(string path)
        {
            Debug.WriteLine($"[フォルダツリー] フォルダ削除: {path}");
            // 各ルートアイテムにイベントを伝播
            foreach (var item in RootItems)
            {
                if (item.OnFolderDeleted(path))
                {
                    break;  // いずれかのアイテムで処理された
                }
            }
        }

        public void OnFileRenamed(string oldPath, string newPath)
        {
            Debug.WriteLine($"[フォルダツリー] フォルダ名変更: {oldPath} -> {newPath}");
            // 各ルートアイテムにイベントを伝播
            foreach (var item in RootItems)
            {
                if (item.OnFolderRenamed(oldPath, newPath))
                {
                    break;  // いずれかのアイテムで処理された
                }
            }
        }

        public void Dispose()
        {
            // すべてのモニターを停止
            foreach (var monitor in _monitors.Values)
            {
                monitor.Dispose();
            }
            _monitors.Clear();

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
                        // ドライブのモニタリングを開始
                        var monitor = new FileSystemMonitor(this, false);  // ドライブ直下のみ監視
                        monitor.StartMonitoring(item.FullPath);
                        _monitors[item.FullPath] = monitor;
                        Debug.WriteLine($"[フォルダツリー] ドライブ監視開始: {item.FullPath}");

                        RootItems.Add(item);
                    }
                }
                else
                {
                    // 特定のパスで初期化
                    ExpandPath(initialPath);
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
                    bool hasSubDirectories = false;
                    try
                    {
                        hasSubDirectories = Directory.GetDirectories(dir)
                            .Where(subDir =>
                            {
                                try
                                {
                                    string subDirName = Path.GetFileName(subDir);
                                    bool isHidden = (File.GetAttributes(subDir) & FileAttributes.Hidden) == FileAttributes.Hidden;
                                    return !isHidden && !subDirName.StartsWith(".");
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
                        // アクセス権がない場合など
                    }

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
                    bool hasSubDirectories = false;
                    try
                    {
                        hasSubDirectories = Directory.GetDirectories(drive.RootDirectory.FullName)
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
                        // アクセス権がない場合など
                    }

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
    }

    /// <summary>
    /// ファイルシステムの項目（ファイルまたはフォルダ）を表すモデル
    /// </summary>
    public class FileSystemItemModel : INotifyPropertyChanged, IDisposable
    {
        private string _name;
        private string _fullPath;
        private bool _canExpand;
        private bool _isExpanded;
        private bool _isSelected;
        private bool _isFolder;
        private ObservableCollection<FileSystemItemModel> _children;
        private bool _isLoading;
        private FileSystemTreeModel _treeModel;
        private FileSystemMonitor? _monitor;

        public FileSystemItemModel(string fullPath, bool isFolder, bool isDummy, FileSystemTreeModel? treeModel = null)
        {
            _fullPath = fullPath;
            _name = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(_name) && isFolder) // ルートディレクトリの場合
            {
                _name = fullPath; // ドライブ名をそのまま使用
            }
            _isFolder = isFolder;
            _children = new ObservableCollection<FileSystemItemModel>();
            IsDummy = isDummy;
            _treeModel = treeModel;

            // フォルダの場合は監視を開始（ダミーは除外）
            if (isFolder && !isDummy && !string.IsNullOrEmpty(fullPath) && treeModel != null)
            {
                StartMonitoring();
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public string FullPath
        {
            get => _fullPath;
            set
            {
                if (_fullPath != value)
                {
                    _fullPath = value;
                    OnPropertyChanged(nameof(FullPath));
                }
            }
        }

        public bool CanExpand
        {
            get => _canExpand;
            set
            {
                if (_canExpand != value)
                {
                    _canExpand = value;
                    OnPropertyChanged(nameof(CanExpand));
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));

                    // 展開時はダミーフォルダを実フォルダに置き換える
                    if (value && IsFolder && CanExpand && _treeModel != null)
                    {
                        // 子フォルダのロード（ダミーの場合のみ）
                        if (Children.Count == 1 && Children[0].IsDummy)
                        {
                            _treeModel.LoadSubFolders(this);
                        }

                        // 監視は常に行われているため、ここでは特に何もしない
                    }
                    // 折りたたみ時は子フォルダをクリアしてダミーに置き換える
                    else if (!value && IsFolder && CanExpand)
                    {
                        // 監視は継続するため、StopMonitoringは呼ばない

                        Children.Clear();
                        Children.Add(new FileSystemItemModel("", true, true));
                    }
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    if (value && _treeModel != null)
                    {
                        // 新しく選択される場合のみ、他のノードの選択をクリア
                        _treeModel.ClearSelection();
                    }
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public bool IsFolder
        {
            get => _isFolder;
            set
            {
                if (_isFolder != value)
                {
                    _isFolder = value;
                    OnPropertyChanged(nameof(IsFolder));
                }
            }
        }

        public ObservableCollection<FileSystemItemModel> Children
        {
            get => _children;
            set
            {
                if (_children != value)
                {
                    _children = value;
                    OnPropertyChanged(nameof(Children));
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged(nameof(IsLoading));
                }
            }
        }

        public bool IsDummy { get; set; }

        /// <summary>
        /// フォルダが作成されたときの処理
        /// </summary>
        /// <param name="path">作成されたフォルダのパス</param>
        /// <returns>このフォルダで処理された場合はtrue</returns>
        public virtual bool OnFolderCreated(string path)
        {
            if (!IsFolder) return false;

            // このフォルダ内に作成されたかチェック（このフォルダ自身が親の場合のみ処理）
            bool isDirectChild = FullPath.Equals(Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase);
            if (!isDirectChild) return false;

            Debug.WriteLine($"[フォルダツリー] 子フォルダ作成: {path}, 親フォルダ: {FullPath}");

            // 展開状態に関わらず、展開可能状態を更新
            CanExpand = true;

            // 展開されている場合は子フォルダを再読み込み
            if (IsExpanded)
            {
                _treeModel?.LoadSubFolders(this);
            }
            // 展開されていない場合はダミー要素を追加
            else if (Children.Count == 0)
            {
                // TreeViewは子ノードがある場合のみ展開ボタンを表示するため、
                // 展開可能なフォルダには必ずダミーの子ノードを追加しておく
                Children.Add(new FileSystemItemModel("", true, true));
            }

            return true;  // 処理完了
        }

        /// <summary>
        /// フォルダが削除されたときの処理
        /// </summary>
        /// <param name="path">削除されたフォルダのパス</param>
        /// <returns>このフォルダで処理された場合はtrue</returns>
        public virtual bool OnFolderDeleted(string path)
        {
            if (!IsFolder) return false;

            // このフォルダ内の削除かチェック（このフォルダ自身が親の場合のみ処理）
            bool isDirectChild = FullPath.Equals(Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase);
            if (!isDirectChild) return false;

            Debug.WriteLine($"[フォルダツリー] 子フォルダ削除: {path}, 親フォルダ: {FullPath}");

            // 展開状態に関わらず、サブフォルダが残っているか確認して展開可能状態を更新
            try
            {
                var subDirs = Directory.GetDirectories(FullPath);
                bool hasSubDirs = subDirs.Length > 0;
                CanExpand = hasSubDirs;

                // 展開されている場合
                if (IsExpanded)
                {
                    // 子フォルダが0になった場合は折りたたむ
                    if (!hasSubDirs)
                    {
                        IsExpanded = false;
                        Children.Clear();
                    }
                    else
                    {
                        // 子フォルダが残っている場合は再読み込み
                        _treeModel?.LoadSubFolders(this);
                    }
                }
                // 展開されていない場合はダミー要素を管理
                else
                {
                    // 子フォルダが0になった場合、子要素をクリア
                    if (!hasSubDirs)
                    {
                        Children.Clear();
                    }
                    // 子フォルダがあるのにダミー要素がない場合はダミー要素を追加
                    else if (Children.Count == 0)
                    {
                        // TreeViewは子ノードがある場合のみ展開ボタンを表示するため、
                        // 展開可能なフォルダには必ずダミーの子ノードを追加しておく
                        Children.Add(new FileSystemItemModel("", true, true));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[フォルダツリー] フォルダ展開可能状態更新エラー: {ex.Message}");
                // エラー時は展開不可に設定
                CanExpand = false;
                if (IsExpanded)
                {
                    IsExpanded = false;
                }
                Children.Clear();
            }

            return true;  // 処理完了
        }

        /// <summary>
        /// フォルダの名前が変更されたときの処理
        /// </summary>
        /// <param name="oldPath">変更前のパス</param>
        /// <param name="newPath">変更後のパス</param>
        /// <returns>このフォルダで処理された場合はtrue</returns>
        public virtual bool OnFolderRenamed(string oldPath, string newPath)
        {
            if (!IsFolder) return false;

            // このフォルダ内のリネームかチェック（このフォルダ自身が親の場合のみ処理）
            bool isDirectChild = FullPath.Equals(Path.GetDirectoryName(oldPath), StringComparison.OrdinalIgnoreCase);
            if (!isDirectChild) return false;

            Debug.WriteLine($"[フォルダツリー] 子フォルダ名変更: {oldPath} -> {newPath}, 親フォルダ: {FullPath}");

            // 展開されている場合は子フォルダを再読み込み
            if (IsExpanded)
            {
                _treeModel?.LoadSubFolders(this);
            }
            // 展開されていない場合は特に何もしない（展開可能状態は変わらない）
            // ただし念のため、ダミー要素の状態を確認
            else if (CanExpand && Children.Count == 0)
            {
                // TreeViewは子ノードがある場合のみ展開ボタンを表示するため、
                // 展開可能なフォルダには必ずダミーの子ノードを追加しておく
                Children.Add(new FileSystemItemModel("", true, true));
            }

            return true;  // 処理完了
        }

        /// <summary>
        /// このフォルダと子フォルダの監視を再帰的に停止します
        /// </summary>
        private void StopMonitoring()
        {
            // このフォルダの監視を終了
            if (_monitor != null)
            {
                _monitor.Dispose();
                _monitor = null;
                Debug.WriteLine($"[フォルダツリー] フォルダ内変更監視終了: {FullPath}");
            }

            // 子フォルダの監視を終了
            foreach (var child in Children)
            {
                if (child.IsFolder && !child.IsDummy)
                {
                    child.StopMonitoring();
                }
            }
        }

        /// <summary>
        /// このフォルダの監視を開始します
        /// </summary>
        private void StartMonitoring()
        {
            if (!IsFolder || IsDummy || string.IsNullOrEmpty(FullPath) || _treeModel == null)
                return;

            try
            {
                if (!Directory.Exists(FullPath))
                    return;

                // 既存の監視があれば停止
                if (_monitor != null)
                {
                    _monitor.Dispose();
                    _monitor = null;
                }

                // 新しい監視を開始
                _monitor = new FileSystemMonitor(_treeModel, true);  // サブディレクトリも監視する
                _monitor.StartMonitoring(FullPath);
                Debug.WriteLine($"[フォルダツリー] フォルダ内変更監視開始: {FullPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[フォルダツリー] 監視開始エラー: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string? propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            StopMonitoring();
        }
    }
}
