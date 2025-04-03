using System.Collections.Generic;
using System.ComponentModel; // INotifyPropertyChanged を使用するために追加
using System.IO; // Path クラスを使用するために追加
using System.Runtime.CompilerServices; // CallerMemberName を使用するために追加

namespace Illustra.Models
{
    /// <summary>
    /// タブの状態を保持するクラス
    /// </summary>
    public class TabState : INotifyPropertyChanged // INotifyPropertyChanged を実装
    {
        private string _folderPath = string.Empty;
        /// <summary>
        /// タブが表示しているフォルダのフルパス
        /// </summary>
        public string FolderPath
        {
            get => _folderPath;
            set => SetProperty(ref _folderPath, value);
        }

        private string? _selectedItemPath;
        /// <summary>
        /// 最後に選択されていたアイテムのフルパス
        /// </summary>
        public string? SelectedItemPath
        {
            get => _selectedItemPath;
            set => SetProperty(ref _selectedItemPath, value);
        }

        private FilterSettings _filterSettings = new FilterSettings();
        /// <summary>
        /// 適用されているフィルタ設定
        /// </summary>
        public FilterSettings FilterSettings
        {
            get => _filterSettings;
            set => SetProperty(ref _filterSettings, value);
        }

        private SortSettings _sortSettings = new SortSettings();
        /// <summary>
        /// 適用されているソート設定
        /// </summary>
        public SortSettings SortSettings
        {
            get => _sortSettings;
            set => SetProperty(ref _sortSettings, value);
        }

        // DisplayName プロパティは TabViewModel で管理するため削除

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// 現在のインスタンスのディープコピーを作成します。
        /// </summary>
        public TabState Clone()
        {
            return new TabState
            {
                FolderPath = this.FolderPath,
                SelectedItemPath = this.SelectedItemPath,
                FilterSettings = this.FilterSettings.Clone(), // FilterSettings の Clone を呼び出す
                SortSettings = this.SortSettings.Clone()     // SortSettings の Clone を呼び出す
            };
        }
    }
}
