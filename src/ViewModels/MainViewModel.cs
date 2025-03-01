using System.ComponentModel;
using System.Runtime.CompilerServices;
using Illustra.Models;
using System.Linq;

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
            if (sortByDate)
            {
                if (ascending)
                {
                    Items = new BulkObservableCollection<FileNodeModel>(Items.OrderBy(i => i.CreationTime));
                }
                else
                {
                    Items = new BulkObservableCollection<FileNodeModel>(Items.OrderByDescending(i => i.CreationTime));
                }
            }
            else
            {
                if (ascending)
                {
                    Items = new BulkObservableCollection<FileNodeModel>(Items.OrderBy(i => i.Name));
                }
                else
                {
                    Items = new BulkObservableCollection<FileNodeModel>(Items.OrderByDescending(i => i.Name));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged = null;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
