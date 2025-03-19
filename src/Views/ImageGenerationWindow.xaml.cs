using System.Windows;
using Illustra.ViewModels;

namespace Illustra.Views
{
    public partial class ImageGenerationWindow : Window
    {
        public ImageGenerationWindow(ImageGenerationWindowViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // TODO: 必要に応じて設定の保存などを実装
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 参照ボタンの処理を実装
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 生成ボタンの処理を実装
        }
    }
}
