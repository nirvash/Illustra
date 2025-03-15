
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
            _children = [];
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
                    }
                    // 折りたたみ時は子フォルダをクリアしてダミーに置き換える
                    else if (!value && IsFolder && CanExpand)
                    {
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
        public virtual void OnFileCreated(string path)
        {
            if (!IsFolder) return;

            // このフォルダ内に作成されたかチェック（このフォルダ自身が親の場合のみ処理）
            bool isDirectChild = FullPath.Equals(Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase);
            if (!isDirectChild) return;

            Debug.WriteLine($"[フォルダツリー] 子フォルダ作成: {path}, 親フォルダ: {FullPath}");

            Application.Current.Dispatcher.Invoke(() =>
            {
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
                var subDirs = Directory.GetDirectories(FullPath);
                bool hasSubDirs = subDirs.Length > 0;

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
        /// フォルダの名前が変更されたときの処理
        /// </summary>
        /// <param name="oldPath">変更前のパス</param>
        /// <param name="newPath">変更後のパス</param>
        /// <returns>このフォルダで処理された場合はtrue</returns>
        /// <summary>
        /// フォルダのパスを更新し、必要に応じて監視を再設定します
        /// </summary>
        /// <param name="newPath">新しいパス</param>
        private void UpdatePath(string newPath)
        {
            FullPath = newPath;
            Name = Path.GetFileName(newPath);
            if (!IsDummy)
            {
                StopMonitoring();
                StartMonitoring();
            }
        }

        public virtual void OnFileRenamed(string oldPath, string newPath)
        {
            if (!IsFolder) return;

            // このフォルダ内のリネームかチェック（このフォルダ自身が親の場合のみ処理）
            bool isDirectChild = FullPath.Equals(Path.GetDirectoryName(oldPath), StringComparison.OrdinalIgnoreCase);
            if (!isDirectChild) return;

            Debug.WriteLine($"[フォルダツリー] 子フォルダ名変更: {oldPath} -> {newPath}, 親フォルダ: {FullPath}");

            Application.Current.Dispatcher.Invoke(() =>
            {
                // 該当する子フォルダを探してパスを更新
                var renamedChild = Children.FirstOrDefault(child =>
                    child.IsFolder && child.FullPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase));

                if (renamedChild != null)
                {
                    renamedChild.UpdatePath(newPath);
                }
                // 展開されていない場合でCanExpandがtrueの時はダミー要素の確認
                else if (!IsExpanded && CanExpand && Children.Count == 0)
                {
                    Children.Add(new FileSystemItemModel("", true, true));
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
