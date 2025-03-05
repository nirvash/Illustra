using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Illustra.Events;
using Illustra.Models;

namespace Illustra.ViewModels
{
    public class FileSystemTreeViewModel : INotifyPropertyChanged
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly FileSystemTreeModel _model;
        private FileSystemItemModel _selectedItem = new FileSystemItemModel("", false, false);
        private bool _isLoading;
        private const string CONTROL_ID = "FileSystemTree";
        private bool _isExpandingPath = false;

        public FileSystemTreeViewModel(IEventAggregator eventAggregator, string? initialPath = null)
        {
            _eventAggregator = eventAggregator;
            _model = new FileSystemTreeModel();

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
            _eventAggregator.GetEvent<FolderSelectedEvent>().Subscribe(OnFolderSelected, ThreadOption.UIThread, false,
                filter => filter.SourceId != CONTROL_ID); // 自分が発信したイベントは無視

            // 初期化
            Initialize(initialPath);
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
                        // 選択されたフォルダのパスをイベントとして発行
                        _eventAggregator.GetEvent<FolderSelectedEvent>().Publish(
                            new FolderSelectedEventArgs(_selectedItem.FullPath, CONTROL_ID));
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

        // 外部からのフォルダ選択イベントの処理
        private void OnFolderSelected(FolderSelectedEventArgs args)
        {
            if (!string.IsNullOrEmpty(args.Path) && Directory.Exists(args.Path))
            {
                Expand(args.Path);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
