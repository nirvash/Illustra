using System.ComponentModel;

namespace Illustra.ViewModels.Settings
{
    public abstract class SettingsViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public abstract void LoadSettings();
        public abstract void SaveSettings();
        public abstract bool ValidateSettings();
    }
}
