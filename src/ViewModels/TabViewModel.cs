using Illustra.Models;
using Prism.Mvvm; // BindableBase を使用するために追加
using System.IO; // Path クラスを使用するために追加
using System.Linq; // Linq を使用するために追加
using Illustra.Helpers; // SettingsHelper を使用するために追加

namespace Illustra.ViewModels
{
    /// <summary>
    /// タブの表示と状態を管理するViewModel
    /// </summary>
    public class TabViewModel : BindableBase
    {
        private TabState _state;
        private readonly AppSettingsModel _appSettings; // お気に入りフォルダリストにアクセスするために追加

        /// <summary>
        /// このタブが保持する状態
        /// </summary>
        public TabState State
        {
            get => _state;
            set
            {
                if (SetProperty(ref _state, value))
                {
                    // 古い State のイベント購読を解除
                    if (_state != null)
                    {
                        _state.PropertyChanged -= State_PropertyChanged;
                    }
                    // 新しい State のイベント購読を開始
                    if (value != null)
                    {
                        value.PropertyChanged += State_PropertyChanged;
                    }
                    // DisplayName も更新される可能性があるため通知
                    RaisePropertyChanged(nameof(DisplayName));
                }
            }
        }

        /// <summary>
        /// タブヘッダーに表示する名前
        /// お気に入りフォルダの場合はその表示名を、そうでなければフォルダ名を表示
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(State?.FolderPath))
                {
                    // TODO: 新規タブや未設定の場合のデフォルト名をリソースから取得する
                    return "New Tab";
                }

                // お気に入りフォルダリストから一致するものを検索
                var favorite = _appSettings?.FavoriteFolders?.FirstOrDefault(f => f.Path == State.FolderPath);
                if (favorite != null && !string.IsNullOrEmpty(favorite.DisplayName))
                {
                    // お気に入りに登録されており、表示名が設定されていればそれを返す
                    return favorite.DisplayName;
                }

                // お気に入りでない、または表示名が未設定の場合はフォルダ名を返す
                try
                {
                    string folderName = Path.GetFileName(State.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    return string.IsNullOrEmpty(folderName) ? State.FolderPath : folderName;
                }
                catch
                {
                    // パスが無効な場合などは元のパスをそのまま表示
                    return State.FolderPath;
                }
            }
        }

        /// <summary>
        /// 表示名を更新します。
        /// </summary>
        public void RefreshDisplayName()
        {
            RaisePropertyChanged(nameof(DisplayName));
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="state">初期状態</param>
        public TabViewModel(TabState state)
        {
            _state = state;
            _appSettings = SettingsHelper.GetSettings(); // AppSettingsを取得

            // FolderPath の変更を監視して DisplayName を更新 (必要であれば)
            // 初期 State のイベント購読を開始
            if (_state != null)
            {
                _state.PropertyChanged += State_PropertyChanged;
            }
            // AppSettings の FavoriteFolders の変更も監視する必要があるが、複雑になるため一旦保留
        }

        /// <summary>
        /// State のプロパティ変更イベントハンドラ
        /// </summary>
        private void State_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // FolderPath が変更されたら DisplayName の変更を通知
            if (e.PropertyName == nameof(TabState.FolderPath))
            {
                RaisePropertyChanged(nameof(DisplayName));
            }
            // TODO: FilterSettings や SortSettings の変更も監視する必要があればここに追加
        }
    }
}
