using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Illustra.ViewModels
{
    public class FolderViewModel : INotifyPropertyChanged
    {
        private string _name;
        public string Name 
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }
        
        public string FullPath { get; set; }
        
        public ObservableCollection<FolderViewModel> SubFolders { get; } = new ObservableCollection<FolderViewModel>();
        
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
