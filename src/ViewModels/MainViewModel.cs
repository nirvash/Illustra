using System.ComponentModel;
using System.Runtime.CompilerServices;
using Illustra.Models;
using System.Linq;
using System.Collections.Immutable;

namespace Illustra.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public BulkObservableCollection<FileNodeModel> Items { get; set; }

        private FileNodeModel? _selectedItem = null;
        public FileNodeModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem != value)
                {
                    _selectedItem = value;
                    OnPropertyChanged();
                    // 選択アイテムが変更されたときの追加処理を行うことも可能
                }
            }
        }

        public MainViewModel()
        {
            Items = new BulkObservableCollection<FileNodeModel>();
        }

        public void SortItems(bool sortByDate, bool ascending)
        {
            var items = Items.ToList();
            if (sortByDate)
            {

                if (ascending)
                {
                    items = items.OrderBy(i => i.CreationTime).ToList();
                }
                else
                {
                    items = items.OrderByDescending(i => i.CreationTime).ToList();
                }
            }
            else
            {
                if (ascending)
                {
                    items = items.OrderBy(i => i.Name).ToList();
                }
                else
                {
                    items = items.OrderByDescending(i => i.Name).ToList();
                }
            }
            Items.ReplaceAll(items);
        }

        public event PropertyChangedEventHandler? PropertyChanged = null;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
