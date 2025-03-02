using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Illustra.Models;
using WpfToolkit.Controls;

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
