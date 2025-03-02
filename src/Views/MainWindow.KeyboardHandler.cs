using System.Windows;

namespace Illustra.Views
{
    // MainWindowクラスのキーボード操作に関するpartialクラス
    public partial class MainWindow : Window
    {
        internal string? GetNextImage(string currentFilePath)
        {
            return ThumbnailList.GetNextImage(currentFilePath);
        }

        internal string? GetPreviousImage(string currentFilePath)
        {
            return ThumbnailList.GetPreviousImage(currentFilePath);
        }

    }
}
