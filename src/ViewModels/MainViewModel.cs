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

        private bool FilterItems(object item)
        {
            if (item is FileNodeModel fileNode)
            {
                // Apply filtering logic here (e.g., based on rating)
                return true; // Change this to apply actual filtering
            }
            return false;
        }

        // rating = -1 はフィルタなし
        public void ApplyRatingFilter(int rating)
        {
            _filteredItems.Filter = item =>
            {
                if (item is FileNodeModel fileNode)
                {
                    return rating == -1 || fileNode.Rating == rating;
                }
                return false;
            };
            _filteredItems.Refresh();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
