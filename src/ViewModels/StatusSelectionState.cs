using System.IO;

namespace Illustra.ViewModels
{
    /// <summary>主ウィンドウのステータスバー向け選択情報。</summary>
    public sealed class StatusSelectionState
    {
        private int _selectedCount;
        private string? _selectedFileName;

        public string? SelectedFileName => _selectedCount == 1 ? _selectedFileName : null;

        public void OnSelectionCountChanged(int selectedCount)
        {
            _selectedCount = selectedCount;
            if (selectedCount == 0 || selectedCount > 1)
                _selectedFileName = null;
        }

        public void OnSelectionCountChanged(int selectedCount, string? selectedFilePath)
        {
            _selectedCount = selectedCount;
            _selectedFileName = selectedCount == 1 && !string.IsNullOrWhiteSpace(selectedFilePath)
                ? Path.GetFileName(selectedFilePath)
                : null;
        }

        public void OnFileSelected(string? fullPath)
        {
            _selectedFileName = string.IsNullOrWhiteSpace(fullPath)
                ? null
                : Path.GetFileName(fullPath);
        }

        public void Clear()
        {
            _selectedCount = 0;
            _selectedFileName = null;
        }
    }
}
