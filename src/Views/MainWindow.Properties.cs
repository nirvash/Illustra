using System.IO;
using System;
using System.Collections.Generic;
using Illustra.Models;

namespace Illustra.Views
{
    public partial class MainWindow
    {
        private bool _isPromptFilterEnabled = false;
        private bool _isTagFilterEnabled = false;
        private int _currentRatingFilter = 0;
        private List<string> _tagFilters = [];

        public bool IsPromptFilterEnabled
        {
            get => _isPromptFilterEnabled;
            set
            {
                if (_isPromptFilterEnabled != value)
                {
                    _isPromptFilterEnabled = value;
                }
            }
        }

        public bool IsTagFilterEnabled
        {
            get => _isTagFilterEnabled;
            set
            {
                if (_isTagFilterEnabled != value)
                {
                    _isTagFilterEnabled = value;
                }
            }
        }

        /// <summary>
        /// 選択されたファイルのプロパティを非同期で読み込み、表示します
        /// </summary>
        private async void LoadFilePropertiesAsync(string filePath)
        {
            try
            {
                var properties = await ImagePropertiesModel.LoadFromFileAsync(filePath);
                // PropertyPanelはXAMLで定義されたコンポーネントで、
                // リンターエラーが表示されることがありますが、ビルド時には問題ありません
                PropertyPanel.ImageProperties = properties;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"プロパティロードエラー: {ex}");
                // PropertyPanelはXAMLで定義されたコンポーネントで、
                // リンターエラーが表示されることがありますが、ビルド時には問題ありません
                PropertyPanel.ImageProperties = new ImagePropertiesModel
                {
                    FileName = Path.GetFileName(filePath),
                    FilePath = filePath,
                };
            }
        }
    }
}
