using System.ComponentModel;
using Prism.Mvvm;

namespace Illustra.Models
{
    public class SelectedFileModel : BindableBase
    {
        public SelectedFileModel(string sourceId, string fullPath)
        {
            SourceId = sourceId;
            FullPath = fullPath;
        }

        public string SourceId { get; set; }

        private string _fullPath;
        public string FullPath
        {
            get => _fullPath;
            set => SetProperty(ref _fullPath, value);
        }
    }
}
