using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Illustra.Models
{
    /// <summary>
    /// ファイルシステムのツリー構造を管理するモデル
    /// </summary>
    public class FileSystemTreeModel : INotifyPropertyChanged
    {
        private ObservableCollection<FileSystemItemModel> _rootItems;
        private bool _isLoading;

        public FileSystemTreeModel(string? initialPath = null)
        {
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
            if (parentItem == null || !parentItem.IsFolder || !parentItem.CanExpand) return;

            try
            {
                // ダミー要素を削除
                parentItem.Children.Clear();
                parentItem.IsLoading = true;

                var subFolders = GetSubFolders(parentItem.FullPath);

                foreach (var folder in subFolders)
                {
                    parentItem.Children.Add(folder);
                }
                parentItem.IsDummy = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"サブフォルダ読み込み中にエラー: {ex.Message}");
            }
            finally
            {
                parentItem.IsLoading = false;
            }
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

                    if (hasSubDirectories)
                    {
                        driveItem.Children.Add(new FileSystemItemModel("", true, true)); // ダミー要素
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

                    if (hasSubDirectories)
                    {
                        item.Children.Add(new FileSystemItemModel("", true, true)); // ダミー要素
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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// ファイルシステムの項目（ファイルまたはフォルダ）を表すモデル
    /// </summary>
    public class FileSystemItemModel : INotifyPropertyChanged
    {
        private string _name;
        private string _fullPath;
        private bool _canExpand;
        private bool _isExpanded;
        private bool _isSelected;
        private bool _isFolder;
        private ObservableCollection<FileSystemItemModel> _children;
        private bool _isLoading;
        private FileSystemTreeModel _parentModel;

        public FileSystemItemModel(string fullPath, bool isFolder, bool isDummy, FileSystemTreeModel? parentModel = null)
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
            _parentModel = parentModel;
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();

                    // 展開時に子フォルダが読み込まれていない場合は読み込む
                    if (value && IsFolder && CanExpand && _parentModel != null)
                    {
                        // ダミー項目がある場合は子フォルダをロード
                        if (Children.Count == 1 && Children[0].IsDummy)
                        {
                            _parentModel.LoadSubFolders(this);
                        }
                        // 子フォルダがまだ一つもない場合も読み込む
                        else if (Children.Count == 0)
                        {
                            // _parentModel.LoadSubFolders(this);
                        }
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
                    _isSelected = value;
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDummy { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
