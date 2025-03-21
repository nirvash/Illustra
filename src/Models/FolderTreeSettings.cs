using System.Collections.Generic;
using Illustra.Helpers;

namespace Illustra.Models
{
    public class FolderTreeSettings
    {
        private const string SettingsFileName = "foldertree_settings.json";

        // フォルダパスをキーとしたソート設定の辞書
        public Dictionary<string, FolderSortSettings> SortSettings { get; set; } = [];

        public void Save() => JsonSettingsHelper.SaveSettings(this, SettingsFileName);

        public static FolderTreeSettings Load() =>
            JsonSettingsHelper.LoadSettings<FolderTreeSettings>(SettingsFileName);

        public FolderSortSettings? GetSortSettings(string folderPath) =>
            SortSettings.TryGetValue(folderPath, out var settings) ? settings : null;
    }

    public class FolderSortSettings
    {
        public SortType SortType { get; set; } = SortType.Name;
        public bool IsAscending { get; set; } = true;
    }

    public enum SortType
    {
        Name,       // フォルダ名順（Win32 StrCmpLogicalWを使用）
        Created     // 作成日時順
    }
}
