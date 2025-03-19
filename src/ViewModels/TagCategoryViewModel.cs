using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using Prism.Mvvm;

namespace Illustra.ViewModels
{
    public class TagCategoryViewModel : BindableBase
    {
        private string _name;
        private string _displayName;
        private Brush _backgroundColor;
        private Brush _borderColor;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        public Brush BackgroundColor
        {
            get => _backgroundColor;
            set => SetProperty(ref _backgroundColor, value);
        }

        public Brush BorderColor
        {
            get => _borderColor;
            set => SetProperty(ref _borderColor, value);
        }

        public ObservableCollection<string> DefaultTags { get; }

        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }

        public TagCategoryViewModel()
        {
            DefaultTags = new ObservableCollection<string>();
            BackgroundColor = Brushes.White;
            BorderColor = Brushes.Gray;
        }
    }
}
