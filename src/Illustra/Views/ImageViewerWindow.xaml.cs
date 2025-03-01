using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Illustra.Views
{
    public partial class ImageViewerWindow : Window
    {
        public string FileName { get; private set; }
        public BitmapSource ImageSource { get; private set; }

        public ImageViewerWindow(string filePath)
        {
            InitializeComponent();
            FileName = System.IO.Path.GetFileName(filePath);
            ImageSource = new BitmapImage(new Uri(filePath));
            DataContext = this;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                Close();
            }
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Close();
        }
    }
}
