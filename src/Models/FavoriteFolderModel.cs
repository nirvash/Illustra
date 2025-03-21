using System.Collections.ObjectModel;

namespace Illustra.Models
{
    public class FavoriteFolderModel
    {
        public string Path { get; set; }
        public string DisplayName
        {
            get
            {
                // パスの末尾のバックスラッシュを削除
                var trimmedPath = Path.TrimEnd('\\');

                // ルートパス（例：C:\）の場合は、パス全体を返す
                if (System.IO.Path.GetPathRoot(trimmedPath)?.Equals(trimmedPath, System.StringComparison.OrdinalIgnoreCase) ?? false)
                {
                    return System.IO.Path.GetPathRoot(Path);
                }

                // 通常のフォルダの場合はフォルダ名を返す
                return System.IO.Path.GetFileName(trimmedPath) ?? trimmedPath;
            }
        }
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
