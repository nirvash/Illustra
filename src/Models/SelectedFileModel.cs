using System.ComponentModel;
using Prism.Mvvm;

namespace Illustra.Models
{
    public class SelectedFileModel : BindableBase
    {
        public SelectedFileModel(string sourceId, string fullPath, int rating)
        {
            SourceId = sourceId;
            FullPath = fullPath;
            Rating = rating;
        }

        public string SourceId { get; set; }

        private string _fullPath;
        public string FullPath
        {
            get => _fullPath;
            set => SetProperty(ref _fullPath, value);
        }

        private int _rating;
        public int Rating
        {
            get => _rating;
            set => SetProperty(ref _rating, value);
        }
    }
}
