using Illustra.Shared.Models.Tools; // Added for McpOpenFolderEvent/Args

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Shared.Models; // Added for MCP events
using Illustra.Views;

namespace Illustra.Models
{
    /// <summary>
    /// ファイルシステムの項目（ファイルまたはフォルダ）を表すモデル
    /// </summary>
    public class FileSystemItemModel : INotifyPropertyChanged, IFileSystemChangeHandler, IDisposable
    {
        private string _name;
        private string _fullPath;
        private bool _canExpand;
        private bool _isExpanded;
        private bool _isSelected;
        private bool _isFolder;
        private bool _isLoading;
        private ObservableCollection<FileSystemItemModel> _children = [];
        private readonly FileSystemTreeModel _treeModel;
        public FileSystemItemModel? Parent { get; private set; }
        private FileSystemMonitor? _monitor;
        private ICommand? _sortTypeCommand;
        private ICommand? _sortDirectionCommand;
        private ICommand? _addToFavoritesCommand;
        private ICommand? _createFolderCommand;
        private ICommand? _renameFolderCommand;
        private ICommand? _deleteFolderCommand;

        public ICommand RenameFolderCommand
        {
            get => _renameFolderCommand ??= new DelegateCommand(ExecuteRenameFolder, CanExecuteRenameFolder);
        }

        private bool CanExecuteRenameFolder()
        {
            return CanRename;
        }

        private async void ExecuteRenameFolder()
        {
            if (!IsFolder) return;

            var dialog = new RenameDialog(FullPath, IsFolder)
            {
                Owner = Application.Current.MainWindow,
                Title = (string)Application.Current.FindResource("String_FileSystemTreeView_RenameTitle")
            };
            if (dialog.ShowDialog() == true)
            {
                string newName = dialog.NewFilePath;
                string newPath = Path.Combine(Path.GetDirectoryName(FullPath), newName);
                if (Directory.Exists(newPath))
                {
                    MessageBox.Show(
                        (string)Application.Current.FindResource("String_FileSystemTreeView_SameNameExists"),
                        (string)Application.Current.FindResource("String_Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                try
                {
                    string oldPath = FullPath;

                    // リネーム前に監視を停止（StopMonitoring は再帰的に子ノードの監視も停止）
                    StopMonitoring();

                    Directory.Move(oldPath, newPath);

                    // 親ノードの OnChildFolderRenamed を呼び出してモデルを更新
                    if (Parent != null)
                    {
                        await Parent.OnChildFolderRenamed(oldPath, newPath);
                    }

                    // パスの更新（子ノードのパスも再帰的に更新）
                    UpdatePath(newPath);

                    // 監視を再開（新しいパスで）
                    if (!IsDummy)
                    {
                        StartMonitoring();
                    }
                }
                catch (Exception ex)
                {
                    var errorMessage = string.Format(
                        (string)Application.Current.FindResource("String_FileSystemTreeView_RenameError"),
                        ex.Message);
                    MessageBox.Show(
                        errorMessage,
                        (string)Application.Current.FindResource("String_Error"), // エラータイトルもリソース化
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }


        public ICommand DeleteFolderCommand
        {
            get => _deleteFolderCommand ??= new DelegateCommand(ExecuteDeleteFolder, CanExecuteDeleteFolder);
        }

        private bool CanExecuteDeleteFolder()
        {
            // ルートフォルダやダミーフォルダは削除不可
            return IsFolder && !IsDummy && Parent != null;
        }

        private async void ExecuteDeleteFolder() // async に変更
        {
            if (!CanExecuteDeleteFolder()) return;

            // 設定から削除モードを取得
            var settings = ViewerSettingsHelper.LoadSettings();
            bool useRecycleBin = settings.DeleteMode == FileDeleteMode.RecycleBin;

            // 確認メッセージを設定
            string messageKey = useRecycleBin
                ? "String_FileSystemTreeView_MoveToRecycleBinConfirmMessage" // ごみ箱移動用のキー
                : "String_FileSystemTreeView_DeleteConfirmMessage"; // 完全削除用のキー
            var message = string.Format((string)Application.Current.FindResource(messageKey), Name);
            var title = (string)Application.Current.FindResource("String_FileSystemTreeView_DeleteConfirmTitle");

            if (MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    // フォルダ存在チェック
                    if (!Directory.Exists(FullPath))
                    {
                        LogHelper.LogWarning($"削除対象のフォルダが見つかりません: {FullPath}"); // LogWarn を LogWarning に修正
                        // 必要であればユーザーに通知 (例: ステータスバー表示)
                        return; // 処理を中断
                    }

                    // 削除前に監視を停止
                    StopMonitoring();

                    // FileOperationHelper を使用してフォルダを削除
                    var fileOpHelper = ContainerLocator.Container.Resolve<FileOperationHelper>();
                    // showUI: false を指定して FileOperationHelper 内の確認ダイアログを抑制
                    await fileOpHelper.DeleteDirectoryAsync(FullPath, useRecycleBin, showUI: false);

                    // OnFileDeleted は FileSystemMonitor が検知するため、ここでは呼び出さない
                    // Parent?.OnFileDeleted(FullPath);
                    // 削除成功後、このノード自体を親から削除する必要があるかもしれないが、
                    // FileSystemMonitor が親の OnFileDeleted を呼び出すことを期待する
                }
                catch (FileOperationHelper.FileOperationException ex)
                {
                    // FileOperationHelper内で既にMessageBoxが表示されている可能性があるため、ここではログのみ記録
                    LogHelper.LogError($"フォルダ削除操作中にエラーが発生しました: {FullPath}. Details: {ex.Message}");
                    // 必要に応じて追加のエラー処理（例：ステータスバーへの表示など）
                }
                catch (Exception ex) // FileOperationHelper 以外からの予期せぬエラー
                {
                    var errorMessage = string.Format(
                        (string)Application.Current.FindResource("String_FileSystemTreeView_DeleteError"),
                        ex.Message);
                    MessageBox.Show(
                        errorMessage,
                        (string)Application.Current.FindResource("String_Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    LogHelper.LogError($"フォルダ削除中に予期せぬエラーが発生しました: {FullPath}. Details: {ex.Message}");
                }
            }
        }

        public ICommand CreateFolderCommand
        {
            get => _createFolderCommand ??= new DelegateCommand(() =>
            {
                if (!IsFolder) return;

                var newFolderName = (string)Application.Current.FindResource("String_FileSystemTreeView_DefaultFolderName");
                var newFolderPath = FileHelper.GenerateUniqueFolderPath(Path.Combine(FullPath, newFolderName));

                try
                {
                    Directory.CreateDirectory(newFolderPath);
                    // フォルダ作成後に親フォルダを展開
                    IsExpanded = true;
                    OnFileCreated(newFolderPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"フォルダ作成中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }
        private bool _lastIsFavorite;

        public ICommand SortTypeCommand
        {
            get => _sortTypeCommand ??= new DelegateCommand<SortType?>(type =>
            {
                if (type.HasValue)
                {
                    IsSortByName = type.Value == SortType.Name;
                    _treeModel.UpdateSort(FullPath, sortType: type);
                }
            });
        }

        public ICommand SortDirectionCommand
        {
            get => _sortDirectionCommand ??= new DelegateCommand<bool?>(ascending =>
            {
                if (ascending.HasValue)
                {
                    IsAscending = ascending.Value;
                    _treeModel.UpdateSort(FullPath, ascending: ascending);
                }
            });
        }

        public ICommand AddToFavoritesCommand
        {
            get => _addToFavoritesCommand ??= new DelegateCommand(
                () => ContainerLocator.Container.Resolve<IEventAggregator>()
                    .GetEvent<AddToFavoritesEvent>()
                    .Publish(FullPath),
                () => !IsFavorite)
                .ObservesProperty(() => IsFavorite);
        }

        public bool IsFavorite
        {
            get
            {
                // FavoriteFolders は FavoriteFolderModel のコレクションになったため、Path プロパティで比較する
                var isFavorite = SettingsHelper.GetSettings()?.FavoriteFolders?.Any(f => f.Path == FullPath) ?? false;
                // 状態が変化したら通知
                if (_lastIsFavorite != isFavorite)
                {
                    _lastIsFavorite = isFavorite;
                    OnPropertyChanged(nameof(IsFavorite));
                }
                return isFavorite;
            }
        }
        private bool _isSortByName = false;
        public bool IsSortByName
        {
            get => _isSortByName;
            set
            {
                if (_isSortByName != value)
                {
                    _isSortByName = value;
                    OnPropertyChanged(nameof(IsSortByName));
                }
            }
        }

        private bool _isAscending = true;
        public bool IsAscending
        {
            get => _isAscending;
            set
            {
                if (_isAscending != value)
                {
                    _isAscending = value;
                    OnPropertyChanged(nameof(IsAscending));
                }
            }
        }

        public FolderSortSettings? GetSortSettings() => _treeModel.GetSortSettings(FullPath);

        public FileSystemItemModel(string fullPath, bool isFolder, bool isDummy, FileSystemTreeModel? treeModel = null, FileSystemItemModel? parent = null)
        {
            _fullPath = fullPath;
            _name = Path.GetFileName(fullPath);
            Parent = parent; // 親を設定
            if (string.IsNullOrEmpty(_name) && isFolder) // ルートディレクトリの場合
            {
                _name = fullPath; // ドライブ名をそのまま使用
            }
            _isFolder = isFolder;
            _children = [];
            IsDummy = isDummy;
            _treeModel = treeModel;

            // フォルダの場合は監視を開始（ダミーは除外）
            if (isFolder && !isDummy && !string.IsNullOrEmpty(fullPath) && treeModel != null)
            {
                StartMonitoring();
            }

            if (!isDummy)
            {
                var sortSettings = GetSortSettings();
                IsSortByName = sortSettings == null || sortSettings.SortType == SortType.Name;
                IsAscending = sortSettings == null || sortSettings.IsAscending;
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

        public bool CanRename => IsFolder && !IsDummy && Parent != null;

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
                    }
                    // 折りたたみ時は子フォルダをクリアしてダミーに置き換える
                    else if (!value && IsFolder && CanExpand)
                    {
                        Children.Clear();
                        Children.Add(new FileSystemItemModel("", true, true, _treeModel, null)); // ダミーは親なし
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
        public virtual void OnFileCreated(string path)
        {
            if (!IsFolder) return;

            // このフォルダ内に作成されたかチェック（このフォルダ自身が親の場合のみ処理）
            bool isDirectChild = FullPath.Equals(Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase);
            if (!isDirectChild) return;

            Debug.WriteLine($"[フォルダツリー] 子フォルダ作成: {path}, 親フォルダ: {FullPath}");

            Application.Current.Dispatcher.Invoke(() =>
            {
                // 追加されるフォルダがアクセス可能で非表示でないことを確認
                if (!_treeModel?.IsAccessibleFolder(path) ?? false)
                {
                    return;
                }

                // サブフォルダの有無を確認して展開可能状態を更新
                CanExpand = _treeModel?.HasSubDirectories(FullPath) ?? false;

                // 展開されている場合は新しいフォルダを追加
                if (IsExpanded)
                {
                    var newItem = _treeModel?.CreateFolderItem(path, this);
                    if (newItem != null)
                    {
                        // 既に存在するかチェック
                        if (Children.Any(child => child.FullPath.Equals(newItem.FullPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            return; // 既に存在する場合は追加しない
                        }

                        // 既存のChildrenコレクションに追加
                        Children.Add(newItem);

                        // ソート設定があれば適用
                        var sortSettings = _treeModel?.GetSortSettings(FullPath);
                        var sortType = sortSettings?.SortType ?? SortType.Name;
                        var isAscending = sortSettings?.IsAscending ?? true;
                        var sorted = FolderSortHelper.Sort(Children, sortType, isAscending);
                        Children = new ObservableCollection<FileSystemItemModel>(sorted);
                    }
                }
                // 展開されていない場合はダミー要素を追加
                else if (Children.Count == 0)
                {
                    // TreeViewは子ノードがある場合のみ展開ボタンを表示するため、
                    // 展開可能なフォルダには必ずダミーの子ノードを追加しておく
                    Children.Add(new FileSystemItemModel("", true, true, null, null)); // ダミーは親なし
                }
            });

            return;  // 処理完了
        }

        /// <summary>
        /// フォルダが削除されたときの処理
        /// </summary>
        /// <param name="path">削除されたフォルダのパス</param>
        /// <returns>このフォルダで処理された場合はtrue</returns>
        public virtual void OnFileDeleted(string path)
        {
            if (!IsFolder) return;

            // このフォルダ内の削除かチェック（このフォルダ自身が親の場合のみ処理）
            bool isDirectChild = FullPath.Equals(Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase);
            if (!isDirectChild) return;

            Debug.WriteLine($"[フォルダツリー] 子フォルダ削除: {path}, 親フォルダ: {FullPath}");

            // 展開状態に関わらず、サブフォルダが残っているか確認して展開可能状態を更新
            try
            {
                bool hasSubDirs = _treeModel?.HasSubDirectories(FullPath) ?? false;

                Application.Current.Dispatcher.Invoke(() =>
                {
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
                            // 削除されたアイテムを見つけて削除
                            var itemToRemove = Children.FirstOrDefault(child =>
                                child.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
                            if (itemToRemove != null)
                            {
                                Children.Remove(itemToRemove);
                            }

                            // ソート設定があれば適用
                            var sortSettings = _treeModel?.GetSortSettings(FullPath);
                            var sortType = sortSettings?.SortType ?? SortType.Name;
                            var isAscending = sortSettings?.IsAscending ?? true;
                            var sorted = FolderSortHelper.Sort(Children, sortType, isAscending);
                            Children = new ObservableCollection<FileSystemItemModel>(sorted);
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
                            Children.Add(new FileSystemItemModel("", true, true, _treeModel, null)); // ダミーは親なし
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[フォルダツリー] フォルダ展開可能状態更新エラー: {ex.Message}");
                // エラー時は展開不可に設定
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CanExpand = false;
                    if (IsExpanded)
                    {
                        IsExpanded = false;
                    }
                    Children.Clear();
                });
            }

            return;  // 処理完了
        }

        /// <summary>
        /// フォルダのパスを更新し、必要に応じて監視を再設定します
        /// </summary>
        /// <param name="newPath">新しいパス</param>
        private void UpdatePath(string newPath)
        {
            FullPath = newPath;
            Name = Path.GetFileName(newPath);

            // 子ノードのパスを再帰的に更新
            foreach (var child in Children)
            {
                if (!child.IsDummy)
                {
                    // 子ノードの新しいパスを計算
                    string childNewPath = Path.Combine(newPath, child.Name);
                    child.UpdatePath(childNewPath);
                }
            }
        }

        /// <summary>
        /// 子フォルダの名前が変更されたときの処理 (外部変更検知 or アプリ内リネーム後に親から呼ばれる)
        /// </summary>
        /// <param name="oldPath">変更前の子フォルダのパス</param>
        /// <param name="newPath">変更後の子フォルダのパス</param>
        public virtual async Task OnChildFolderRenamed(string oldPath, string newPath)
        {
            if (!IsFolder) return;

            // このフォルダ内のリネームかチェック（このフォルダ自身が親の場合のみ処理）
            bool isDirectChild = FullPath.Equals(Path.GetDirectoryName(oldPath), StringComparison.OrdinalIgnoreCase);
            if (!isDirectChild) return;

            Debug.WriteLine($"[フォルダツリー] 子フォルダ名変更: {oldPath} -> {newPath}, 親フォルダ: {FullPath}");

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                // データベースのパスを更新
                var db = ContainerLocator.Container.Resolve<DatabaseManager>();
                await db.UpdateFolderPathsAsync(oldPath, newPath);

                // 該当する子フォルダを探す
                var renamedChild = Children.FirstOrDefault(child =>
                    child.IsFolder && child.FullPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase));

                if (renamedChild != null)
                {
                    // 監視を停止
                    renamedChild.StopMonitoring();

                    // パスを更新
                    renamedChild.UpdatePath(newPath);

                    // 監視を再開
                    if (!renamedChild.IsDummy)
                    {
                        renamedChild.StartMonitoring();
                    }

                    // ソート設定があれば適用
                    var sortSettings = _treeModel?.GetSortSettings(FullPath);
                    var sortType = sortSettings?.SortType ?? SortType.Name;
                    var isAscending = sortSettings?.IsAscending ?? true;
                    var sorted = FolderSortHelper.Sort(Children, sortType, isAscending);
                    Children = new ObservableCollection<FileSystemItemModel>(sorted);

                    // 現在選択されているアイテムがリネームの影響を受けるか確認
                    var currentSelectedItem = _treeModel?.SelectedItem;
                    if (currentSelectedItem != null &&
                        (currentSelectedItem.FullPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase) ||
                         currentSelectedItem.FullPath.StartsWith(oldPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    {
                        string newSelectedPath;
                        if (currentSelectedItem.FullPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
                        {
                            // リネームされたフォルダ自身が選択されていた場合
                            newSelectedPath = newPath;
                        }
                        else
                        {
                            // リネームされたフォルダの子孫が選択されていた場合
                            newSelectedPath = newPath + currentSelectedItem.FullPath.Substring(oldPath.Length);
                        }

                        // FolderSelected イベントを発行して選択パスを更新
                        var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                        eventAggregator.GetEvent<McpOpenFolderEvent>().Publish( // Renamed
                            new McpOpenFolderEventArgs // Renamed
                            {
                                FolderPath = newSelectedPath,
                                SourceId = "FileSystemItemModel", // Identify the source
                                ResultCompletionSource = null // No need to wait for result here
                            });
                        Debug.WriteLine($"[フォルダツリー] リネームにより選択フォルダ変更イベント発行: {newSelectedPath}");
                    }
                }
                // 展開されていない場合でCanExpandがtrueの時はダミー要素の確認
                else if (!IsExpanded && CanExpand && Children.Count == 0)
                {
                    Children.Add(new FileSystemItemModel("", true, true, _treeModel, null)); // ダミーは親なし
                }
            });

            return;  // 処理完了
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
                _monitor = new FileSystemMonitor(this, true);  // サブディレクトリも監視する
                _monitor.StartMonitoring(FullPath);

                // 子フォルダの監視も再帰的に開始
                foreach (var child in Children)
                {
                    if (child.IsFolder && !child.IsDummy)
                    {
                        child.StartMonitoring();
                    }
                }
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
