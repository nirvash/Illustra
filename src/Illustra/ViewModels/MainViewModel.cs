using System.ComponentModel;
using System.Runtime.CompilerServices;
using Illustra.Models;

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

        public event PropertyChangedEventHandler? PropertyChanged = null;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
