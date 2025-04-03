using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows; // Clipboard クラスを使用するために追加
using System.Windows.Data;
using Illustra.Models;
using Illustra.Helpers;
using System.Collections.Specialized;
using System.Diagnostics;
using Illustra.Views;
using System.Linq;
using System.IO;
using Illustra.Events;
using System.Windows.Input;

namespace Illustra.ViewModels
{
    public class ThumbnailListViewModel : INotifyPropertyChanged
    {
        private bool _sortByDate;
        private bool _sortAscending;
        public BulkObservableCollection<FileNodeModel> Items { get; } = new();
        private ICollectionView _filteredItems;
        private IEventAggregator _eventAggregator = null!;
        private DatabaseManager _db = null!;

        private string _currentFolderPath = string.Empty;

        public bool SortByDate
        {
            get => _sortByDate;
            set
            {
                if (_sortByDate != value)
                {
                    _sortByDate = value;
                    OnPropertyChanged(nameof(SortByDate));
                }
            }
        }

        public bool SortAscending
        {
            get => _sortAscending;
            set
            {
                if (_sortAscending != value)
                {
                    _sortAscending = value;
                    OnPropertyChanged(nameof(SortAscending));
                }
            }
        }

        public ICommand CopyCommand { get; }
        public ICommand PasteCommand { get; }
        public ICommand SelectAllCommand { get; }


        public ThumbnailListViewModel()
        {
            // 初期設定を読み込む
            var settings = SettingsHelper.GetSettings();
            _sortByDate = settings.SortByDate;
            _sortAscending = settings.SortAscending;

            _filteredItems = CollectionViewSource.GetDefaultView(Items);
            _filteredItems.Filter = FilterItems;

            _db = ContainerLocator.Container.Resolve<DatabaseManager>();
            _eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _eventAggregator.GetEvent<RatingChangedEvent>().Subscribe(OnRatingChanged);

            // SelectedItemsの変更通知を設定
            _selectedItems.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(SelectedItems));
                UpdateLastSelectedFlag();
                RaiseCommandCanExecuteChanged(); // コマンド状態を更新
            };

            // コマンドの初期化
            CopyCommand = new RelayCommand(ExecuteCopy, CanExecuteCopy);
            PasteCommand = new RelayCommand(ExecutePaste, CanExecutePaste);
            SelectAllCommand = new RelayCommand(ExecuteSelectAll, CanExecuteSelectAll);

            // SelectedTabChangedEvent を購読
            _eventAggregator.GetEvent<SelectedTabChangedEvent>().Subscribe(OnSelectedTabChanged, ThreadOption.UIThread);
        }

        // レーティングをデータベースに永続化する
        private void OnRatingChanged(RatingChangedEventArgs args)
        {
            // 変更されたアイテムを取得
            var item = Items.FirstOrDefault(x => x.FullPath.Equals(args.FilePath, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                // UI 側で直接モデルのレーティングを更新してるケースがあるため、ここで変更がなくても永続化が必要
                item.Rating = args.Rating;
                _ = _db.UpdateRatingAsync(item);
            }
        }

        // SelectedItems も BulkObservableCollection を使用する
        private readonly BulkObservableCollection<FileNodeModel> _selectedItems = [];
        public BulkObservableCollection<FileNodeModel> SelectedItems => _selectedItems;

        public ICollectionView FilteredItems => _filteredItems;

        public void ClearItems()
        {
            Items.Clear();
            SelectedItems.Clear();
            RaiseCommandCanExecuteChanged(); // 選択状態変更時にコマンド状態を更新
        }

        public void AddItem(FileNodeModel item)
        {
            var index = FindSortedInsertIndex(item);
            Items.Insert(index, item);
            RaiseCommandCanExecuteChanged(); // コマンド状態を更新
        }

        private int FindSortedInsertIndex(FileNodeModel newItem)
        {
            if (Items.Count == 0) return 0;

            // ビューモデルのソート設定を使用
            bool sortByDate = _sortByDate;
            bool sortAscending = _sortAscending;

            for (int i = 0; i < Items.Count; i++)
            {
                var currentItem = Items[i];
                int comparison;

                if (sortByDate)
                {
                    // 日付でソート
                    comparison = newItem.CreationTime.CompareTo(currentItem.CreationTime);
                }
                else
                {
                    // ファイル名でソート
                    comparison = string.Compare(newItem.FileName, currentItem.FileName, StringComparison.CurrentCultureIgnoreCase);
                }

                // 降順の場合は比較結果を反転
                if (!sortAscending)
                {
                    comparison = -comparison;
                }

                if (comparison <= 0)
                {
                    return i;
                }
            }

            return Items.Count;
        }

        public void SortItems(bool sortByDate, bool sortAscending)
        {
            // メモリ上でソートを行う
            var sortedItems = Items.ToList();
            SortHelper.SortFileNodes(sortedItems, sortByDate, sortAscending);
            Items.ReplaceAll(sortedItems);

            RaiseCommandCanExecuteChanged(); // コマンド状態を更新
            // フィルタ条件が変わっていないので、フィルタの Refresh は不要
        }

        // ThumbnailListControlからの呼び出し用に非同期メソッドを残す
        public Task SortItemsAsync(bool sortByDate, bool sortAscending)
        {
            SortItems(sortByDate, sortAscending);
            return Task.CompletedTask;
        }


        private int _currentRatingFilter = 0;
        private bool _isPromptFilterEnabled = false;
        private readonly Dictionary<string, bool> _promptCache = [];
        private readonly Dictionary<string, List<string>> _tagCache = [];
        private List<string> _tagFilters = [];
        private bool _isTagFilterEnabled;
        private List<string> _extensionFilters = []; // 追加
        private bool _isExtensionFilterEnabled; // 追加

        /// <summary>
        /// 現在適用されているレーティングフィルタの値を取得します
        /// </summary>
        public int CurrentRatingFilter
        {
            get => _currentRatingFilter;
            set
            {
                if (_currentRatingFilter != value)
                {
                    _currentRatingFilter = value;
                    OnPropertyChanged(nameof(CurrentRatingFilter));
                    OnPropertyChanged(nameof(IsRatingFilterActive));
                }
            }
        }

        /// <summary>
        /// レーティングフィルタが有効かどうかを示す値を取得します
        /// </summary>
        public bool IsRatingFilterActive => _currentRatingFilter > 0;

        /// <summary>
        /// プロンプトフィルタが有効かどうかを示す値を取得します
        /// </summary>
        public bool IsPromptFilterEnabled
        {
            get => _isPromptFilterEnabled;
            private set
            {
                if (_isPromptFilterEnabled != value)
                {
                    _isPromptFilterEnabled = value;
                    _promptCache.Clear();
                    OnPropertyChanged(nameof(IsPromptFilterEnabled));
                }
            }
        }

        /// <summary>
        /// タグフィルタが有効かどうかを示す値を取得します
        /// </summary>
        public bool IsTagFilterEnabled
        {
            get => _isTagFilterEnabled;
            private set
            {
                if (_isTagFilterEnabled != value)
                {
                    _isTagFilterEnabled = value;
                    OnPropertyChanged(nameof(IsTagFilterEnabled));
                }
            }
        }

        /// <summary>
        /// 拡張子フィルタが有効かどうかを示す値を取得します
        /// </summary>
        public bool IsExtensionFilterEnabled
        {
            get => _isExtensionFilterEnabled;
            private set
            {
                if (_isExtensionFilterEnabled != value)
                {
                    _isExtensionFilterEnabled = value;
                    OnPropertyChanged(nameof(IsExtensionFilterEnabled));
                }
            }
        }

        /// <summary>
        /// フィルタ条件に基づいてアイテムをフィルタリングします
        /// </summary>
        private bool FilterItems(object item)
        {
            if (item is not FileNodeModel fileNode)
                return false;

            // レーティングフィルタ
            if (CurrentRatingFilter > 0 && fileNode.Rating != CurrentRatingFilter)
                return false;

            // タグフィルタ
            if (IsTagFilterEnabled)
            {
                if (!_tagCache.TryGetValue(fileNode.FullPath, out var tags) || tags == null || tags.Count == 0)
                    return false;

                // すべてのタグが含まれているかチェック（AND検索）
                foreach (var tag in _tagFilters)
                {
                    bool tagFound = false;
                    foreach (var fileTag in tags)
                    {
                        if (fileTag.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            tagFound = true;
                            break;
                        }
                    }

                    // 1つでも見つからないタグがあれば、このファイルはフィルタを通過しない
                    if (!tagFound)
                        return false;
                }
            }

            // プロンプトフィルタ
            if (IsPromptFilterEnabled)
            {
                // プロンプトが空の場合は非表示
                if (!_promptCache.TryGetValue(fileNode.FullPath, out bool hasPrompt) || !hasPrompt)
                    return false;
            }

            // 拡張子フィルタ (追加)
            if (IsExtensionFilterEnabled && _extensionFilters.Any())
            {
                string fileExtension = Path.GetExtension(fileNode.FullPath)?.ToLowerInvariant();
                if (string.IsNullOrEmpty(fileExtension) || !_extensionFilters.Contains(fileExtension))
                {
                    return false;
                }
            }

            return true;
        }

        //
        public void RefreshFiltering()
        {
            ApplyFilterAndUpdateSelection();
        }

        /// <summary>
        /// フィルタ処理を適用し、結果に基づいて選択状態を更新します
        /// </summary>
        private void ApplyFilterAndUpdateSelection()
        {
            Debug.WriteLine("[フィルタ処理後処理] ApplyFilterAndUpdateSelection");
            // フィルタ適用前の選択状態を保持 (Refresh でクリアされることがあるため)
            var previousSelected = _selectedItems.LastOrDefault();

            // フィルタを適用
            _filteredItems.Refresh();
            OnPropertyChanged(nameof(FilteredItems));

            UpdateSelectionAfterFilter(previousSelected);
        }


        public async Task UpdatePromptCacheAsync(string filePath)
        {
            try
            {
                var properties = await ImagePropertiesModel.LoadFromFileAsync(filePath);
                _promptCache[filePath] = properties.HasStableDiffusionData;
            }
            catch
            {
                _promptCache[filePath] = false;
            }
        }

        /// <summary>
        /// 指定されたファイルのタグ情報をキャッシュに読み込みます
        /// </summary>
        public async Task UpdateTagCacheAsync(string filePath)
        {
            try
            {
                var properties = await ImagePropertiesModel.LoadFromFileAsync(filePath);
                var allTags = new List<string>();

                // 通常のタグを追加
                if (properties?.StableDiffusionResult != null && properties.StableDiffusionResult.Tags.Count > 0)
                {
                    allTags.AddRange(properties.StableDiffusionResult.Tags);
                }

                // Loraタグを追加
                if (properties?.StableDiffusionResult != null && properties.StableDiffusionResult.Loras.Count > 0)
                {
                    allTags.AddRange(properties.StableDiffusionResult.Loras);
                }

                _tagCache[filePath] = allTags;
            }
            catch
            {
                _tagCache[filePath] = new List<string>();
            }
        }

        /// <summary>
        /// レーティングフィルターとタグフィルターを適用します
        /// </summary>
        public async Task ApplyAllFilters(int ratingFilter, bool isPromptFilterEnabled, List<string> tagFilters, bool isTagFilterEnabled, List<string> extensionFilters, bool isExtensionFilterEnabled) // 引数追加
        {
            // レーティングフィルタの設定
            CurrentRatingFilter = ratingFilter;

            // プロンプトフィルタの設定
            IsPromptFilterEnabled = isPromptFilterEnabled;
            _promptCache.Clear();

            // タグフィルタの設定（新しいリストのインスタンスを作成）
            _tagFilters = new List<string>(tagFilters);
            _isTagFilterEnabled = isTagFilterEnabled;
            _tagCache.Clear();

            // 拡張子フィルタの設定 (追加)
            _extensionFilters = new List<string>(extensionFilters);
            IsExtensionFilterEnabled = isExtensionFilterEnabled;

            // キャッシュの差分更新は未実装
            // 別スレッドでタグキャッシュを更新
            await Task.Run(async () =>
            {
                var itemsCopy = Items.ToList(); // スレッドセーフのためにコピーを作成
                foreach (var item in itemsCopy)
                {
                    if (isTagFilterEnabled)
                    {
                        await UpdateTagCacheAsync(item.FullPath);
                    }
                    if (isPromptFilterEnabled)
                    {
                        await UpdatePromptCacheAsync(item.FullPath);
                    }
                }

                // キャッシュ更新完了後にUIスレッドでフィルタを更新
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    ApplyFilterAndUpdateSelection();
                });
            });
        }


        /// <summary>
        /// すべてのフィルターをクリアします
        /// フォルダ選択時の状態更新用
        /// </summary>
        public void ClearAllFilters()
        {
            CurrentRatingFilter = 0;
            IsPromptFilterEnabled = false;
            IsTagFilterEnabled = false;
            _tagFilters.Clear();
        }


        public string CurrentFolderPath
        {
            get => _currentFolderPath;
            set
            {
                if (SetProperty(ref _currentFolderPath, value)) // propertyName is automatically set by CallerMemberName
                {
                    // フォルダパス変更時にPasteコマンドの状態を更新
                    (PasteCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        #region Command Implementations

        private void ExecuteCopy()
        {
            _eventAggregator.GetEvent<RequestCopyEvent>().Publish();
        }

        private bool CanExecuteCopy()
        {
            return SelectedItems.Any();
        }

        private void ExecutePaste()
        {
            _eventAggregator.GetEvent<RequestPasteEvent>().Publish();
        }

        private bool CanExecutePaste()
        {
            // フォルダパスが空でなく、かつクリップボードにファイルまたは画像データが含まれている場合に有効
            return !string.IsNullOrEmpty(CurrentFolderPath) &&
                   (Clipboard.ContainsFileDropList() || Clipboard.ContainsImage());
        }

        private void ExecuteSelectAll()
        {
            _eventAggregator.GetEvent<RequestSelectAllEvent>().Publish();
        }

        private bool CanExecuteSelectAll()
        {
            return Items.Any();
        }

        // コマンドのCanExecute状態を更新するためのヘルパーメソッド
        private void RaiseCommandCanExecuteChanged()
        {
            (CopyCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PasteCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SelectAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        private void UpdateSelectionAfterFilter(FileNodeModel? previousSelected)
        {
            Debug.WriteLine("[フィルタ処理後処理] UpdateSelectionAfterFilter");
            _selectedItems.Clear();

            // フィルタ後のアイテムを取得
            var filteredItems = _filteredItems.Cast<FileNodeModel>().ToList();

            if (filteredItems.Count > 0)
            {
                if (previousSelected != null && filteredItems.Any(x => x.FullPath.Equals(previousSelected.FullPath)))
                {
                    // 以前選択されていたアイテムがフィルタ後も存在する場合は、
                    // フィルタ後のリストから該当アイテムを取得して選択
                    var matchingItem = filteredItems.First(x => x.FullPath.Equals(previousSelected.FullPath));
                    _selectedItems.Add(matchingItem);
                }
                else
                {
                    // フィルタ後のリストの先頭アイテムを選択
                    _selectedItems.Add(filteredItems[0]);
                }
            }

            // 選択状態の変更を通知
            OnPropertyChanged(nameof(SelectedItems));
        }

        private void UpdateLastSelectedFlag()
        {
            // すべてのアイテムのフラグをリセット
            foreach (var item in Items)
            {
                item.IsLastSelected = false;
            }

            // 選択アイテムがある場合のみ、最後のアイテムのフラグを設定
            var lastSelectedItem = _selectedItems.LastOrDefault();
            if (lastSelectedItem != null)
            {
                lastSelectedItem.IsLastSelected = true;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            // Items または SelectedItems が変更されたらコマンド状態を更新
            if (propertyName == nameof(Items) || propertyName == nameof(SelectedItems))
            {
                RaiseCommandCanExecuteChanged();
            }
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }


        /// <summary>
        /// 指定されたタブの状態をViewModelに適用します。
        /// </summary>
        /// <param name="state">適用するタブの状態</param>
        public async Task ApplyTabState(TabState state)
        {
            if (state == null) return;

            // 1. ソート設定を適用
            SortByDate = state.SortSettings.SortByDate;
            SortAscending = state.SortSettings.SortAscending;

            // 2. フィルタ設定を適用 (ApplyAllFilters を呼び出す)
            // ApplyAllFilters は内部でキャッシュ更新とフィルタ適用を行う
            // フォルダ読み込み前にフィルタ条件を設定しておく
            await ApplyAllFilters(
                state.FilterSettings.Rating,
                state.FilterSettings.HasPrompt,
                state.FilterSettings.Tags,
                state.FilterSettings.Tags.Any(), // isTagFilterEnabled
                state.FilterSettings.Extensions,
                state.FilterSettings.Extensions.Any() // isExtensionFilterEnabled
            );

            // 3. フォルダパスを適用 (これによりアイテムの再読み込みがトリガーされる想定)
            // CurrentFolderPath の setter がアイテム読み込みロジックを呼び出すか確認が必要
            if (CurrentFolderPath != state.FolderPath)
            {
                CurrentFolderPath = state.FolderPath;
                // TODO: CurrentFolderPath の setter がアイテム読み込みをトリガーしない場合、
                // ここで明示的にアイテム読み込み処理を呼び出す必要がある。
                // 例: await LoadItemsForCurrentFolderAsync();
            }
            // else
            // {
            //     // フォルダパスが同じ場合、フィルタとソートは既に適用済みのはず
            //     // 必要であればここで明示的に Refresh や Sort を呼ぶことも検討
            // }

            // 4. 選択状態を復元
            if (!string.IsNullOrEmpty(state.SelectedItemPath))
            {
                var itemToSelect = Items.FirstOrDefault(i => i.FullPath.Equals(state.SelectedItemPath, StringComparison.OrdinalIgnoreCase));
                if (itemToSelect != null)
                {
                    // TODO: ThumbnailListControl 側の選択処理を呼び出すか、イベントを発行する必要がある
                    // 例: _eventAggregator.GetEvent<SelectFileRequestEvent>().Publish(itemToSelect.FullPath);
                    //     または、SelectedItems プロパティを直接操作する (UI連動に注意)
                    SelectedItems.Clear();
                    SelectedItems.Add(itemToSelect);
                }
                else
                {
                    SelectedItems.Clear(); // 見つからない場合は選択解除
                }
            }
            else
            {
                SelectedItems.Clear(); // 選択パスがなければ選択解除
            }
        }


        /// <summary>
        /// SelectedTabChangedEvent を受信したときの処理
        /// </summary>
        /// <summary>
        /// フォルダの内容を読み込みます
        /// </summary>
        public async Task LoadFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                // 現在のフォルダパスを更新
                CurrentFolderPath = path;

                // アイテムをクリア
                Items.Clear();

                // フォルダ内の画像ファイルを列挙
                var files = Directory.GetFiles(path)
                    .Where(file => FileHelper.SupportedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    .ToList();

                // FileNodeModel に変換
                var fileNodes = files.Select(file => new FileNodeModel
                {
                    FullPath = file,
                    FileName = Path.GetFileName(file),
                    CreationTime = File.GetCreationTime(file)
                }).ToList();

                // アイテムに追加
                Items.AddRange(fileNodes);

                // ソートを適用
                SortItems(SortByDate, SortAscending);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フォルダの読み込み中にエラーが発生: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// タブ状態が変更されたときの処理
        /// </summary>
        private async void OnSelectedTabChanged(SelectedTabChangedEventArgs args)
        {
            if (args?.NewTabState != null)
            {
                await ApplyTabState(args.NewTabState);

                // フォルダ内容の読み込み
                if (!string.IsNullOrEmpty(args.NewTabState.FolderPath))
                {
                    await LoadFolder(args.NewTabState.FolderPath);
                }
            }
            else
            {
                // 選択されたタブがない場合
                Items.Clear();
                CurrentFolderPath = string.Empty;
            }
        }

    }
}
