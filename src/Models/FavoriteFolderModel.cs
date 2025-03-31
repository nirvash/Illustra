using System.Collections.ObjectModel;
using System.ComponentModel; // INotifyPropertyChanged を使うために追加
using System.Runtime.CompilerServices; // CallerMemberName を使うために追加
using System.IO; // Path クラスを使うために追加

namespace Illustra.Models
{
    public class FavoriteFolderModel : INotifyPropertyChanged // INotifyPropertyChanged を実装
    {
        private string _path;
        private string _displayName; // 表示名を保持するフィールド

        public string Path
        {
            get => _path;
            set
            {
                if (_path != value)
                {
                    _path = value;
                    OnPropertyChanged();
                    // Path が変わったら、DisplayName が設定されていない場合は DisplayMember も更新する必要がある
                    if (string.IsNullOrEmpty(_displayName))
                    {
                        OnPropertyChanged(nameof(DisplayMember));
                    }
                    // Tooltip 用に Path 自身も通知
                    OnPropertyChanged(nameof(Path));
                }
            }
        }

        // 設定可能な表示名プロパティ
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayMember)); // DisplayMember も更新通知
                    OnPropertyChanged(nameof(HasDisplayName)); // HasDisplayName も更新通知
                }
            }
        }

        // UI表示用のプロパティ
        public string DisplayMember
        {
            get
            {
                // DisplayName が設定されていればそれを返す
                if (!string.IsNullOrEmpty(DisplayName))
                {
                    return DisplayName;
                }

                // DisplayName がなければ、パスからフォルダ名を生成して返す (従来のロジック)
                if (string.IsNullOrEmpty(Path)) return string.Empty; // Path が null または空の場合

                // パスの末尾の区切り文字を削除
                var trimmedPath = Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

                // ルートパス（例：C:\）の場合は、パス全体を返す
                try // GetPathRoot は無効なパスで例外を投げる可能性がある
                {
                    string root = System.IO.Path.GetPathRoot(trimmedPath);
                    if (root != null && root.Equals(trimmedPath, System.StringComparison.OrdinalIgnoreCase))
                    {
                        // GetPathRoot は末尾に区切り文字を付ける場合があるので、元のPathから取得する
                        return System.IO.Path.GetPathRoot(Path);
                    }
                }
                catch (ArgumentException)
                {
                    // 無効なパスの場合はそのまま返す
                    return Path;
                }


                // 通常のフォルダの場合はフォルダ名を返す
                return System.IO.Path.GetFileName(trimmedPath) ?? trimmedPath;
            }
        }

        // DisplayName が設定されているかどうかを示すプロパティ (ContextMenu の IsEnabled に使う)
        [Newtonsoft.Json.JsonIgnore] // 設定ファイルには保存しない
        public bool HasDisplayName => !string.IsNullOrEmpty(DisplayName);

        // Children プロパティは現状不要そうなのでコメントアウト (必要なら戻す)
        // public ObservableCollection<FavoriteFolderModel> Children { get; } = new ObservableCollection<FavoriteFolderModel>();

        // コンストラクタ (displayName をオプション引数に追加)
        // Newtonsoft.Json がデシリアライズ時に使用する可能性があるため JsonConstructor 属性を付与
        [Newtonsoft.Json.JsonConstructor]
        public FavoriteFolderModel(string path, string displayName = null)
        {
            // Path プロパティのセッター経由で設定
            Path = path;
            // DisplayName プロパティのセッター経由で設定
            DisplayName = displayName;
        }

        // 引数なしのコンストラクタ (XAMLのデザイナや一部のシリアライザで必要になる場合がある)
        public FavoriteFolderModel() { }


        public override string ToString()
        {
            return DisplayMember; // 表示用のプロパティを返す
        }

        // INotifyPropertyChanged の実装
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
