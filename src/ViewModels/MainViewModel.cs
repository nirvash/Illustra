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
        }

        private FileNodeModel? _selectedItem;
        public FileNodeModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem != value)
                {
                    _selectedItem = value;
                    OnPropertyChanged(nameof(SelectedItem));
                }
            }
        }

        public ICollectionView FilteredItems => _filteredItems;

        public void ClearItems()
        {
            Items.Clear();
            SelectedItem = null;
        }

        public void AddItem(FileNodeModel item)
        {
            Items.Add(item);
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

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
