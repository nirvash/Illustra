using System.IO;
using System.Windows;
using Illustra.Helpers;
using MahApps.Metro.Controls;

namespace Illustra.Views
{
    public partial class RenameDialog : MetroWindow
    {
        public string NewFilePath { get; private set; }

        private readonly string _oldFilePath;
        private readonly bool _isFolder;

        public RenameDialog(string oldFilePath, bool isFolder)
        {
            InitializeComponent();
            _oldFilePath = oldFilePath;
            _isFolder = isFolder;

            if (_isFolder)
            {
                // フォルダの場合
                FileNameTextBox.Text = Path.GetFileName(oldFilePath);
                FileExtensionTextBlock.Text = string.Empty;
                FileExtensionTextBlock.Visibility = Visibility.Collapsed;
            }
            else
            {
                // ファイルの場合
                var fileName = Path.GetFileNameWithoutExtension(oldFilePath);
                var extension = Path.GetExtension(oldFilePath);
                FileNameTextBox.Text = fileName;
                FileExtensionTextBlock.Text = extension;
            }

            // ファイル名部分を全選択
            FileNameTextBox.SelectAll();
            FileNameTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var directory = Path.GetDirectoryName(_oldFilePath);
                var newFileName = _isFolder ? FileNameTextBox.Text : FileNameTextBox.Text + FileExtensionTextBlock.Text;
                var newFilePath = Path.Combine(directory, newFileName);

                if (newFilePath.Equals(_oldFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    // ファイル名が変更されていない場合は何もしない
                    DialogResult = false;
                    Close();
                    return;
                }

                if (!FileHelper.IsValidFileName(newFileName))
                {
                    MessageBox.Show("無効なファイル名です。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    FileNameTextBox.Focus();
                    FileNameTextBox.SelectAll();
                    return;
                }

                // 重複チェック
                if ((_isFolder && Directory.Exists(newFilePath)) || (!_isFolder && File.Exists(newFilePath)))
                {
                    int counter = 1;
                    string baseFileName = Path.GetFileNameWithoutExtension(newFileName);
                    string extension = Path.GetExtension(newFileName);

                    do
                    {
                        newFilePath = Path.Combine(directory, $"{baseFileName} ({counter}){extension}");
                        counter++;
                    } while (File.Exists(newFilePath));

                    // 確認ダイアログを表示
                    var result = MessageBox.Show(
                        $"{Path.GetFileName(_oldFilePath)} を {Path.GetFileName(newFilePath)} に変更しますか？\n\nこのフォルダには同じファイルが既にあります。",
                        "確認",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.OK)
                    {
                        FileNameTextBox.Focus();
                        FileNameTextBox.SelectAll();
                        return;
                    }
                }

                NewFilePath = newFilePath;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
