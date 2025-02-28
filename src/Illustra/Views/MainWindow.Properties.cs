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
                await LoadFileProperties(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"プロパティロードエラー: {ex}");
                ClearPropertiesDisplay();
                PropFileName.Text = Path.GetFileName(filePath);
                PropFilePath.Text = filePath;
                PropFileSize.Text = "読み込みエラー";
            }
        }

        private async Task LoadFileProperties(string filePath)
        {
            // プロパティ表示をリセット
            ClearPropertiesDisplay();

            // プロパティを非同期で読み込み
            var properties = await ImagePropertiesModel.LoadFromFileAsync(filePath);

            // UIを更新
            PropFileName.Text = properties.FileName;
            PropFilePath.Text = properties.FilePath;
            PropFileSize.Text = properties.FileSizeFormatted;
            PropCreatedDate.Text = properties.CreatedDate.ToString("yyyy/MM/dd HH:mm:ss");
            PropModifiedDate.Text = properties.ModifiedDate.ToString("yyyy/MM/dd HH:mm:ss");

            PropResolution.Text = properties.Resolution;
            PropFormat.Text = properties.ImageFormat;
            PropColorDepth.Text = properties.ColorDepth;

            PropCamera.Text = properties.CameraModel;

            if (properties.DateTaken.HasValue)
                PropDateTaken.Text = properties.DateTaken.Value.ToString("yyyy/MM/dd HH:mm:ss");

            PropExposureTime.Text = properties.ExposureTime;

            string fnumberIso = "";
            if (!string.IsNullOrEmpty(properties.FNumber))
                fnumberIso += properties.FNumber;
            if (!string.IsNullOrEmpty(properties.ISOSpeed))
            {
                if (!string.IsNullOrEmpty(fnumberIso))
                    fnumberIso += " / ";
                fnumberIso += properties.ISOSpeed;
            }
            PropFNumberISO.Text = fnumberIso;
        }

        private void ClearPropertiesDisplay()
        {
            PropFileName.Text = string.Empty;
            PropFilePath.Text = string.Empty;
            PropFileSize.Text = string.Empty;
            PropCreatedDate.Text = string.Empty;
            PropModifiedDate.Text = string.Empty;

            PropResolution.Text = string.Empty;
            PropFormat.Text = string.Empty;
            PropColorDepth.Text = string.Empty;

            PropCamera.Text = string.Empty;
            PropDateTaken.Text = string.Empty;
            PropExposureTime.Text = string.Empty;
            PropFNumberISO.Text = string.Empty;
        }
    }
}
