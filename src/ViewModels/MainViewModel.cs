using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using Illustra.Models;
using Illustra.Helpers;
using System.Collections.Specialized;

namespace Illustra.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseManager _db;
        private string? _currentFolderPath;
        private bool _sortByDate;
        private bool _sortAscending;
        private double _thumbnailSize = 200.0; // デフォルトサイズ

        public double ThumbnailSize
        {
            get => _thumbnailSize;
            set
            {
                if (_thumbnailSize != value)
                {
                    _thumbnailSize = value;
                    OnPropertyChanged(nameof(ThumbnailSize));
                }
            }
        }

        public BulkObservableCollection<FileNodeModel> Items { get; } = new();
        private ICollectionView _filteredItems;

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

        public MainViewModel(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));

            // 初期設定を読み込む
            var settings = SettingsHelper.GetSettings();
            _sortByDate = settings.SortByDate;
            _sortAscending = settings.SortAscending;

            _filteredItems = CollectionViewSource.GetDefaultView(Items);
            _filteredItems.Filter = FilterItems;

            // SelectedItemsの変更通知を設定
            _selectedItems.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(SelectedItems));
                UpdateLastSelectedFlag();
            };
        }

        private readonly ObservableCollection<FileNodeModel> _selectedItems = new();
        public ObservableCollection<FileNodeModel> SelectedItems => _selectedItems;

        // 通知なしで選択状態を更新するメソッド
        public void UpdateSelectedItemsSilently(IEnumerable<FileNodeModel> items)
        {
            _selectedItems.Clear();
            foreach (var item in items)
            {
                _selectedItems.Add(item);
            }
        }

        // 単一アイテムの追加・削除用メソッド
        public void AddSelectedItemSilently(FileNodeModel item)
        {
            _selectedItems.Add(item);
        }

        public void RemoveSelectedItemSilently(FileNodeModel item)
        {
            _selectedItems.Remove(item);
        }

        public ICollectionView FilteredItems => _filteredItems;

        public void ClearItems()
        {
            Items.Clear();
            SelectedItems.Clear();
        }

        public void AddItem(FileNodeModel item)
        {
            var index = FindSortedInsertIndex(item);
            Items.Insert(index, item);
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

        public async Task SortItemsAsync(bool sortByDate, bool sortAscending)
        {
            if (string.IsNullOrEmpty(_currentFolderPath)) return;

            var sortedItems = await _db.GetSortedFileNodesAsync(_currentFolderPath, sortByDate, sortAscending);
            Items.ReplaceAll(sortedItems);
        }

        public void SetCurrentFolder(string path)
        {
            _currentFolderPath = path;

            // フォルダ変更時にタグキャッシュをクリア
            _tagCache.Clear();
        }

        private int _currentRatingFilter = 0;
        private bool _isPromptFilterEnabled = false;
        private readonly Dictionary<string, bool> _promptCache = [];
        private readonly Dictionary<string, List<string>> _tagCache = [];
        private List<string> _tagFilters = [];
        private bool _isTagFilterEnabled;

        /// <summary>
        /// 現在適用されているレーティングフィルタの値を取得します
        /// </summary>
        public int CurrentRatingFilter
        {
            get => _currentRatingFilter;
            private set
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
                _promptCache[filePath] = !string.IsNullOrEmpty(properties?.UserComment) &&
                                            (properties?.HasStableDiffusionData ?? false);
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
                if (properties?.StableDiffusionResult != null && properties.StableDiffusionResult.Tags.Count > 0)
                {
                    _tagCache[filePath] = properties.StableDiffusionResult.Tags;
                }
                else
                {
                    _tagCache[filePath] = new List<string>();
                }
            }
            catch
            {
                _tagCache[filePath] = new List<string>();
            }
        }

        /// <summary>
        /// レーティングフィルターとタグフィルターを適用します
        /// </summary>
        public async Task ApplyAllFilters(int ratingFilter, bool isPromptFilterEnabled, List<string> tagFilters, bool isTagFilterEnabled)
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

        public event PropertyChangedEventHandler? PropertyChanged;
        private void UpdateSelectionAfterFilter(FileNodeModel? previousSelected)
        {
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
        }
    }
}
