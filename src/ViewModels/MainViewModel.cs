using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using Illustra.Models;
using Illustra.Helpers;

namespace Illustra.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseManager _db = new();
        private string? _currentFolderPath;
        public BulkObservableCollection<FileNodeModel> Items { get; } = new();
        private ICollectionView _filteredItems;

        public MainViewModel()
        {
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

            // 現在のソート条件を取得（ThumbnailListControl から）
            var settings = SettingsHelper.GetSettings();
            bool sortByDate = settings.SortByDate;
            bool sortAscending = settings.SortAscending;

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
        }

        private int _currentRatingFilter = 0;

        private bool FilterItems(object item)
        {
            if (item is FileNodeModel fileNode)
            {
                // レーティングフィルターが適用されていない場合は全て表示
                if (_currentRatingFilter <= 0)
                    return true;

                // 単一レーティングのみを表示
                return fileNode.Rating == _currentRatingFilter;
            }
            return false;
        }

        /// <summary>
        /// レーティングフィルターを適用します
        /// </summary>
        /// <param name="rating">フィルターするレーティング値。0はフィルターなし</param>
        public void ApplyRatingFilter(int rating)
        {
            _currentRatingFilter = rating;
            _filteredItems.Refresh();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
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
