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
        /// <summary>
        /// ウィンドウ全体でのキー入力を処理するハンドラ
        /// </summary>
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // ThumbnailItemsControlが有効な場合、キー操作を処理
            if (_viewModel.Items.Count > 0 &&
                (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down ||
                 e.Key == Key.Home || e.Key == Key.End || e.Key == Key.Enter))
            {
                // ウィンドウレベルでキー処理をする前に、ListViewにフォーカスを与える
                ThumbnailItemsControl.Focus();

                // Enterキーが押されて選択アイテムがある場合はビューアを表示
                if (e.Key == Key.Enter && _viewModel.SelectedItem != null)
                {
                    ShowImageViewer(_viewModel.SelectedItem.FullPath);
                    e.Handled = true;
                    return;
                }

                // 直接ThumbnailItemsControl_KeyDownメソッドを呼び出して処理
                ThumbnailItemsControl_KeyDown(ThumbnailItemsControl, e);

                // イベントが処理されたことを示す
                if (e.Handled)
                {
                    return;
                }
            }
        }

        private void ThumbnailItemsControl_KeyDown(object sender, KeyEventArgs e)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(ThumbnailItemsControl);
            if (scrollViewer == null) return;

            var panel = FindVisualChild<VirtualizingWrapPanel>(scrollViewer);
            if (panel == null) return;

            var selectedIndex = ThumbnailItemsControl.SelectedIndex;
            if (selectedIndex == -1 && _viewModel.Items.Count > 0)
            {
                // 選択がない場合は先頭を選択
                selectedIndex = 0;
            }
            if (selectedIndex == -1) return;

            int itemsPerRow = Math.Max(1, (int)(panel.ActualWidth / (ThumbnailSizeSlider.Value + 6))); // 6はマージン
            int totalItems = ThumbnailItemsControl.Items.Count;
            int totalRows = (totalItems + itemsPerRow - 1) / itemsPerRow;
            Debug.WriteLine($"selected: {selectedIndex}, total: {totalItems}, rows: {totalRows}");

            FileNodeModel? targetItem = null;

            switch (e.Key)
            {
                case Key.Enter:
                    if (_viewModel.SelectedItem != null)
                    {
                        ShowImageViewer(_viewModel.SelectedItem.FullPath);
                        e.Handled = true;
                        return;
                    }
                    break;

                case Key.Home:
                    // 先頭アイテムに移動
                    targetItem = _viewModel.Items[0];
                    e.Handled = true;
                    break;

                case Key.End:
                    // 最後のアイテムに移動
                    targetItem = _viewModel.Items[totalItems - 1];
                    e.Handled = true;
                    break;

                case Key.Right:
                    if (selectedIndex + 1 < totalItems)
                    {
                        targetItem = _viewModel.Items[selectedIndex + 1];
                        e.Handled = true;
                    }
                    else if (!e.IsRepeat) // キーリピートでない場合のみ循環
                    {
                        // 最後のアイテムで右キーを押したとき、先頭に循環
                        targetItem = _viewModel.Items[0];
                        e.Handled = true;
                    }
                    break;

                case Key.Left:
                    // 左端かどうかチェック
                    if (selectedIndex % itemsPerRow == 0)
                    {
                        // 左端で左キーを押したとき
                        // 前の行の番号を計算
                        int prevRow = (selectedIndex / itemsPerRow) - 1;

                        // 負の行番号にならないようにチェック（先頭行の場合）
                        if (prevRow >= 0)
                        {
                            // 前の行の右端のインデックスを計算
                            int targetIndex = (prevRow * itemsPerRow) + (itemsPerRow - 1);

                            // 存在するアイテム数を超えないように制限
                            targetIndex = Math.Min(targetIndex, totalItems - 1);
                            targetItem = _viewModel.Items[targetIndex];
                            e.Handled = true;
                        }
                        else if (!e.IsRepeat) // キーリピートでない場合のみ循環
                        {
                            // 先頭行の左端の場合は、最終行の右端に移動（循環ナビゲーション）
                            int lastRow = (totalItems - 1) / itemsPerRow;
                            int lastRowItemCount = totalItems - (lastRow * itemsPerRow);
                            int targetIndex = (lastRow * itemsPerRow) + Math.Min(itemsPerRow, lastRowItemCount) - 1;
                            targetItem = _viewModel.Items[targetIndex];
                            e.Handled = true;
                        }
                    }
                    else
                    {
                        // 通常の左キー処理
                        if (selectedIndex - 1 >= 0)
                        {
                            targetItem = _viewModel.Items[selectedIndex - 1];
                            e.Handled = true;
                        }
                    }
                    break;

                case Key.Up:
                    // 上の行の同じ列の位置を計算
                    if (selectedIndex >= itemsPerRow)
                    {
                        // 通常の上移動
                        int targetIndex = selectedIndex - itemsPerRow;
                        targetItem = _viewModel.Items[targetIndex];
                        e.Handled = true;
                    }
                    else if (!e.IsRepeat) // キーリピートでない場合のみ循環
                    {
                        // 最上段から最下段へ循環
                        int currentColumn = selectedIndex % itemsPerRow;
                        int lastRowStartIndex = ((totalItems - 1) / itemsPerRow) * itemsPerRow;
                        int targetIndex = Math.Min(lastRowStartIndex + currentColumn, totalItems - 1);
                        targetItem = _viewModel.Items[targetIndex];
                        e.Handled = true;
                    }
                    break;

                case Key.Down:
                    // 下の行の同じ列の位置を計算
                    int nextRowIndex = selectedIndex + itemsPerRow;
                    if (nextRowIndex < totalItems)
                    {
                        // 通常の下移動
                        targetItem = _viewModel.Items[nextRowIndex];
                        e.Handled = true;
                    }
                    else if (!e.IsRepeat) // キーリピートでない場合のみ循環
                    {
                        // 最下段から最上段へ循環
                        int currentColumn = selectedIndex % itemsPerRow;
                        int targetIndex = Math.Min(currentColumn, totalItems - 1);
                        targetItem = _viewModel.Items[targetIndex];
                        e.Handled = true;
                    }
                    break;
            }

            if (e.Handled && targetItem != null)
            {
                e.Handled = true; // イベントを確実に処理済みとしてマーク

                var index = _viewModel.Items.IndexOf(targetItem);
                Debug.WriteLine($"target: {index}, path: {targetItem.FullPath}");

                // ViewModelを通じて選択を更新
                _viewModel.SelectedItem = targetItem;
                _currentSelectedFilePath = targetItem.FullPath;
                LoadFilePropertiesAsync(targetItem.FullPath);

                // スクロール処理とフォーカス処理
                ThumbnailItemsControl.ScrollIntoView(targetItem);
            }
        }
    }
}
