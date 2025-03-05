using System.Collections.ObjectModel;
using System.Windows;
using Illustra.Helpers;

namespace Illustra.Views
{
    public partial class TestWindow : Window, IFileSystemChangeHandler
    {
        private readonly FileSystemMonitor _monitor;
        public ObservableCollection<string> Files { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> LogMessages { get; } = new ObservableCollection<string>();

        public TestWindow()
        {
            InitializeComponent();
            _monitor = new FileSystemMonitor(this);
            FilesList.ItemsSource = Files;
            LogView.Text = string.Join("\n", LogMessages);
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _monitor.StartMonitoring(FolderPath.Text);
                LogMessages.Add($"Started monitoring: {FolderPath.Text}");
            }
            catch (Exception ex)
            {
                LogMessages.Add($"Error: {ex.Message}");
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _monitor.StopMonitoring();
            LogMessages.Add("Stopped monitoring");
        }

        public void OnFileCreated(string path)
        {
            Dispatcher.Invoke(() => Files.Add(path));
            LogMessages.Add($"File created: {path}");
        }

        public void OnFileDeleted(string path)
        {
            Dispatcher.Invoke(() => Files.Remove(path));
            LogMessages.Add($"File deleted: {path}");
        }

        public void OnFileRenamed(string oldPath, string newPath)
        {
            Dispatcher.Invoke(() =>
            {
                Files.Remove(oldPath);
                Files.Add(newPath);
            });
            LogMessages.Add($"File renamed: {oldPath} to {newPath}");
        }
    }
}
