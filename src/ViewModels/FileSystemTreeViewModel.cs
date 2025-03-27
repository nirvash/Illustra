using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Models;
using Illustra.Shared.Models; // Added for MCP events
using GongSolutions.Wpf.DragDrop;
using System.Windows;
using Microsoft.VisualBasic;

namespace Illustra.ViewModels
{
    public class FileSystemTreeViewModel : INotifyPropertyChanged, IFileSystemChangeHandler
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly FileSystemTreeModel _model;
        private readonly FolderTreeSettings _folderSettings;
        private FileSystemItemModel _selectedItem = new("", false, true);
        private bool _isLoading;
        private const string CONTROL_ID = "FileSystemTree";
        private bool _isExpandingPath = false;

        public FileSystemTreeViewModel(IEventAggregator eventAggregator, string? initialPath = null)
        {
            // 基本的な初期化
            _eventAggregator = eventAggregator;
            _folderSettings = FolderTreeSettings.Load();
            _model = new FileSystemTreeModel(_folderSettings);

            // モデルのプロパティ変更を監視
            _model.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(FileSystemTreeModel.IsLoading))
                {
                    IsLoading = _model.IsLoading;
                }
            };

            // コマンドの初期化
            AddToFavoritesCommand = new DelegateCommand<FileSystemItemModel>(
                item =>
                {
                    if (item != null && item.IsFolder)
                    {
                        _eventAggregator.GetEvent<AddToFavoritesEvent>().Publish(item.FullPath);
                    }
                },
                item => item != null && item.IsFolder
            );

            RemoveFromFavoritesCommand = new DelegateCommand<FileSystemItemModel>(
                item =>
                {
                    if (item != null && item.IsFolder)
                    {
                        _eventAggregator.GetEvent<RemoveFromFavoritesEvent>().Publish(item.FullPath);
                    }
                },
                item => item != null && item.IsFolder
            );

            // ツリーアイテム展開用コマンド
            ExpandItemCommand = new DelegateCommand<FileSystemItemModel>(item =>
            {
                if (item != null && item.IsFolder && item.CanExpand)
                {
                    _model.LoadSubFolders(item);
                }
            });

            // イベント購読
            // _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected, ThreadOption.UIThread, false,
            //     filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視
            _eventAggregator.GetEvent<McpOpenFolderEvent>().Subscribe(OnMcpOpenFolder, ThreadOption.UIThread);


            // 初期化
            Initialize(initialPath);
        }

        public void OnFileCreated(string path)
        {
            if (Directory.Exists(path))
            {
                Debug.WriteLine($"フォルダ作成を検知: {path}");
            }
        }

        public void OnFileDeleted(string path)
        {
            Debug.WriteLine($"フォルダ削除を検知: {path}");
        }

        public async Task OnChildFolderRenamed(string oldPath, string newPath)
        {
            Debug.WriteLine($"子フォルダ名変更を検知: {oldPath} -> {newPath}");
            // Model側の処理に任せて、ViewModelでは監視のみ行う
            await Task.CompletedTask;
        }

        private void Initialize(string? initialPath)
        {
            _model.Initialize(initialPath);
        }

        // モデルのRootItemsを公開
        public ObservableCollection<FileSystemItemModel> RootItems => _model.RootItems;

        public FileSystemItemModel SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem != value)
                {
                    _selectedItem = value;
                    OnPropertyChanged();

                    // コマンドの有効状態を更新
                    ((DelegateCommand<FileSystemItemModel>)AddToFavoritesCommand).RaiseCanExecuteChanged();
                    ((DelegateCommand<FileSystemItemModel>)RemoveFromFavoritesCommand).RaiseCanExecuteChanged();

                    if (_selectedItem != null && _selectedItem.IsFolder && !_isExpandingPath)
                    {
                        // Publish McpOpenFolderEvent instead
                        _eventAggregator.GetEvent<McpOpenFolderEvent>().Publish(
                            new McpOpenFolderEventArgs
                            {
                                FolderPath = _selectedItem.FullPath,
                                SourceId = CONTROL_ID, // Identify the source as this control
                                ResultCompletionSource = null // No need to wait for result from UI interaction
                            });
                    }
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

        // コマンド
        public ICommand AddToFavoritesCommand { get; }
        public ICommand RemoveFromFavoritesCommand { get; }
        public ICommand ExpandItemCommand { get; }

        // 指定したパスを展開する
        public void Expand(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
            {
                _model.Initialize(null);
                return;
            }

            _model.ExpandPath(targetPath);
        }

        // お気に入りへの追加
        private void AddToFavorites()
        {
            if (SelectedItem != null && SelectedItem.IsFolder)
            {
                _eventAggregator.GetEvent<AddToFavoritesEvent>().Publish(SelectedItem.FullPath);
            }
        }

        private bool CanAddToFavorites()
        {
            return SelectedItem != null && SelectedItem.IsFolder;
        }

        // お気に入りからの削除
        private void RemoveFromFavorites()
        {
            if (SelectedItem != null && SelectedItem.IsFolder)
            {
                _eventAggregator.GetEvent<RemoveFromFavoritesEvent>().Publish(SelectedItem.FullPath);
            }
        }

        private bool CanRemoveFromFavorites()
        {
            return SelectedItem != null && SelectedItem.IsFolder;
        }

        // MCPからのフォルダ選択イベントの処理
        private void OnMcpOpenFolder(McpOpenFolderEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine($"[FileSystemTreeVM] Received McpOpenFolderEvent for path: {args.FolderPath}, SourceId: {args.SourceId}");
            bool success = false;
            try
            {
                if (!string.IsNullOrEmpty(args.FolderPath) && Directory.Exists(args.FolderPath))
                {
                    Expand(args.FolderPath);
                    // フォルダを展開した後、そのノードを画面内に表示
                    _eventAggregator.GetEvent<BringTreeItemIntoViewEvent>().Publish(args.FolderPath);

                    // File selection logic removed as per user feedback
                    success = true;
                }
            }
            catch (Exception ex)
            {
                // Log the exception
                Debug.WriteLine($"Error handling McpOpenFolderEvent: {ex.Message}");
                success = false;
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine($"[FileSystemTreeVM] Setting McpOpenFolderEvent result: {success}");
                // Notify the APIService about the result
                args.ResultCompletionSource?.SetResult(success);
                System.Diagnostics.Debug.WriteLine($"[FileSystemTreeVM] McpOpenFolderEvent result set.");
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public FileSystemItemModel? FindItem(string path)
        {
            return _model.FindItem(path);
        }
    }
}
