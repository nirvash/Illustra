using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Illustra.ViewModels
{
    public class FolderViewModel : INotifyPropertyChanged
    {
        private string _name = string.Empty; // 初期値を設定してnull警告を解消
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
        
        public string FullPath { get; set; } = string.Empty; // 初期値を設定してnull警告を解消
        
        public ObservableCollection<FolderViewModel> SubFolders { get; } = new ObservableCollection<FolderViewModel>();
        
        public event PropertyChangedEventHandler? PropertyChanged; // Nullableに変更して警告を解消
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
