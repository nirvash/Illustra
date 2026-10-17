using System.Collections.Generic;
using System.ComponentModel; // INotifyPropertyChanged を使用するために追加
using System.IO; // Path クラスを使用するために追加
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices; // CallerMemberName を使用するために追加
using Illustra.Helpers; // 診断ログ用

namespace Illustra.Models
{
    /// <summary>
    /// タブの状態を保持するクラス
    /// </summary>
    public class TabState : INotifyPropertyChanged // INotifyPropertyChanged を実装
    {
        private bool _isMcpTab;
        public bool IsMcpTab
        {
            get => _isMcpTab;
            set => SetProperty(ref _isMcpTab, value);
        }

        private string _folderPath = string.Empty;
        /// <summary>
        /// タブが表示しているフォルダのフルパス
        /// </summary>
        public string FolderPath
        {
            get => _folderPath;
            set
            {
                if (_folderPath != value)
                {
                    NavigationDiagnosticsLog.Append(
                        $"FolderPath CHANGED: \"{_folderPath}\" -> \"{value}\"", withStackTrace: true);
                }
                SetProperty(ref _folderPath, value);
            }
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

        public const int MaxFolderHistory = 100;

        /// <summary>このタブで訪れたフォルダ。設定保存時にタブと一緒に復元する。</summary>
        public List<string> FolderHistory { get; set; } = new List<string>();

        public int FolderHistoryIndex { get; set; } = -1;

        [JsonIgnore]
        public bool CanGoBack => FolderHistoryIndex > 0;
        [JsonIgnore]
        public bool CanGoForward => FolderHistoryIndex >= 0 && FolderHistoryIndex < FolderHistory.Count - 1;

        public void RecordFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (FolderHistory == null) FolderHistory = new List<string>();
            if (FolderHistoryIndex < 0 || FolderHistoryIndex >= FolderHistory.Count ||
                !string.Equals(FolderHistory[FolderHistoryIndex], FolderPath, System.StringComparison.OrdinalIgnoreCase))
            {
                FolderHistory.Clear();
                FolderHistoryIndex = -1;
                if (!string.IsNullOrWhiteSpace(FolderPath))
                {
                    FolderHistory.Add(FolderPath);
                    FolderHistoryIndex = 0;
                }
            }
            if (string.Equals(FolderPath, path, System.StringComparison.OrdinalIgnoreCase)) return;
            if (FolderHistoryIndex < FolderHistory.Count - 1)
                FolderHistory.RemoveRange(FolderHistoryIndex + 1, FolderHistory.Count - FolderHistoryIndex - 1);
            FolderHistory.Add(path);
            FolderHistoryIndex = FolderHistory.Count - 1;
            TrimFolderHistory();
        }

        public void TrimFolderHistory()
        {
            if (FolderHistory == null) FolderHistory = new List<string>();
            int excess = FolderHistory.Count - MaxFolderHistory;
            if (excess <= 0) return;
            // 復元済みの過大な履歴では、現在位置だけは捨てない。
            int removeBefore = System.Math.Min(excess, System.Math.Max(0, FolderHistoryIndex));
            FolderHistory.RemoveRange(0, removeBefore);
            FolderHistoryIndex -= removeBefore;
            excess -= removeBefore;
            if (excess > 0)
                FolderHistory.RemoveRange(FolderHistory.Count - excess, excess);
        }

        public string MoveFolderHistory(int direction)
        {
            int target = FolderHistoryIndex + direction;
            if (direction != -1 && direction != 1 || FolderHistory == null ||
                target < 0 || target >= FolderHistory.Count) return null;
            FolderHistoryIndex = target;
            return FolderHistory[target];
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
