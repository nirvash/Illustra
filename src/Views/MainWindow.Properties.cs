using System.IO;
using Illustra.Models;

namespace Illustra.Views
{
    public partial class MainWindow
    {

        /// <summary>
        /// 選択されたファイルのプロパティを非同期で読み込み、表示します
        /// </summary>
        private async void LoadFilePropertiesAsync(string filePath)
        {
            try
            {
                var properties = await ImagePropertiesModel.LoadFromFileAsync(filePath);
                PropertyPanel.ImageProperties = properties;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"プロパティロードエラー: {ex}");
                PropertyPanel.ImageProperties = new ImagePropertiesModel
                {
                    FileName = Path.GetFileName(filePath),
                    FilePath = filePath,
                };
            }
        }
    }
}
