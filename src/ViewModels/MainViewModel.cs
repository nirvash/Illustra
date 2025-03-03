using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Illustra.Models;
using Illustra.Helpers;

namespace Illustra.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseManager _db = new();
        private string? _currentFolderPath;
        public BulkObservableCollection<FileNodeModel> Items { get; } = new();

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

        public async Task FilterByRatingAsync(int rating)
        {
            if (string.IsNullOrEmpty(_currentFolderPath)) return;

            var filteredItems = await _db.GetFileNodesByRatingAsync(_currentFolderPath, rating);
            Items.ReplaceAll(filteredItems);
        }

        public void SetCurrentFolder(string path)
        {
            _currentFolderPath = path;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
