using System.Collections.ObjectModel;

namespace Illustra.Models
{
    public class FavoriteFolderModel
    {
        public string Path { get; set; }
        public string DisplayName => System.IO.Path.GetFileName(Path.TrimEnd('\\')) ?? Path;
        public ObservableCollection<FavoriteFolderModel> Children { get; } = new ObservableCollection<FavoriteFolderModel>();

        public FavoriteFolderModel(string path)
        {
            Path = path;
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
