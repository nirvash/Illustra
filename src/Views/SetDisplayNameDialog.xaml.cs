using Illustra.Models; // FavoriteFolderModel を使うために追加
using System.Windows;
using MahApps.Metro.Controls; // MetroWindow を使うために追加
using System.ComponentModel; // INotifyPropertyChanged を使うために追加
using System.Runtime.CompilerServices; // CallerMemberName を使うために追加

namespace Illustra.Views
{
    /// <summary>
    /// SetDisplayNameDialog.xaml の相互作用ロジック
    /// </summary>
    public partial class SetDisplayNameDialog : MetroWindow, INotifyPropertyChanged // INotifyPropertyChanged を実装
    {
        // 呼び出し元が結果を取得するためのプロパティ
        public string ResultDisplayName { get; private set; }

        // フルパス表示用プロパティ
        private string _folderPath;
        public string FolderPath
        {
            get => _folderPath;
            // Setter は不要だが、変更通知のために追加（デバッグ目的）
            private set
            {
                if (_folderPath != value)
                {
                    _folderPath = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _editableDisplayName;
        // XAML からバインドする編集用プロパティ
        public string EditableDisplayName
        {
            get => _editableDisplayName;
            set
            {
                if (_editableDisplayName != value)
                {
                    _editableDisplayName = value;
                    OnPropertyChanged(); // 変更通知
                }
            }
        }


        public SetDisplayNameDialog(FavoriteFolderModel folder)
        {
            InitializeComponent();
            DataContext = this; // DataContext を自分自身に設定

            // プロパティ経由で設定
            FolderPath = folder.Path;

            // EditableDisplayName を初期化
            // DisplayName が空なら DisplayMember (フォルダ名) を、そうでなければ DisplayName を使う
            EditableDisplayName = string.IsNullOrEmpty(folder.DisplayName) ? folder.DisplayMember : folder.DisplayName;

            // TextBox に初期フォーカスを設定
            Loaded += (sender, e) =>
            {
                DisplayNameTextBox.Focus();
                DisplayNameTextBox.SelectAll(); // 初期値を全選択状態にする
            };
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // 編集結果を ResultDisplayName プロパティに保存
            ResultDisplayName = EditableDisplayName;
            DialogResult = true; // OK ボタンがクリックされたことを示す
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false; // キャンセルされたことを示す
        }

        // INotifyPropertyChanged の実装
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
