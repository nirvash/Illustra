using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Illustra.Models;
using GongSolutions.Wpf.DragDrop;
using System.IO;

namespace Illustra.Helpers
{
    public class DragDropHelper
    {
        public static List<string> GetDroppedFiles(IDropInfo dropInfo)
        {
            // ドロップされたファイルのパスを取得
            var files = new List<string>();
            if (dropInfo.Data is FileNodeModel droppedItem)
            {
                files.Add(droppedItem.FullPath);
            }
            else if (dropInfo.Data != null)
            {
                var objArray = dropInfo.Data as IEnumerable<object>;
                var nodeArray = objArray.OfType<FileNodeModel>().ToArray();
                foreach (var node in nodeArray)
                {
                    files.Add(node.FullPath);
                }
            }
            return files;
        }

        public static bool IsSameDirectory(List<string> files, string targetPath)
        {
            foreach (var file in files)
            {
                string parentDir = Path.GetDirectoryName(file);
                if (string.Equals(parentDir, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// ドラッグ操作の視覚効果を作成します
        /// </summary>
        public UIElement CreateDragVisual(IList<FileNodeModel> selectedItems)
        {
            if (selectedItems == null || selectedItems.Count == 0)
                return new UIElement();

            // 単一選択の場合
            if (selectedItems.Count == 1)
            {
                var item = selectedItems[0];
                if (item.ThumbnailInfo?.Thumbnail == null)
                    return CreateTextVisual(item.FileName);

                return CreateThumbnailVisual(item.ThumbnailInfo.Thumbnail, item.FileName);
            }

            // 複数選択の場合は重なり合った表示を作成
            return CreateMultipleVisual(selectedItems.Count);
        }

        /// <summary>
        /// テキストのみの視覚効果を作成
        /// </summary>
        private UIElement CreateTextVisual(string fileName)
        {
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = fileName,
                Background = Brushes.White,
                Padding = new Thickness(5),
                Margin = new Thickness(2),
                MaxWidth = 200,
                TextWrapping = TextWrapping.Wrap
            };

            var border = new System.Windows.Controls.Border
            {
                Child = textBlock,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 3,
                    ShadowDepth = 2,
                    Opacity = 0.5
                }
            };

            return border;
        }

        /// <summary>
        /// サムネイル付きの視覚効果を作成
        /// </summary>
        private UIElement CreateThumbnailVisual(BitmapSource thumbnail, string fileName)
        {
            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var image = new System.Windows.Controls.Image
            {
                Source = thumbnail,
                Width = 120,
                Height = 120,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(2)
            };
            System.Windows.Controls.Grid.SetRow(image, 0);

            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = fileName,
                TextAlignment = TextAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)),
                Padding = new Thickness(2),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 120
            };
            System.Windows.Controls.Grid.SetRow(textBlock, 1);

            grid.Children.Add(image);
            grid.Children.Add(textBlock);

            var border = new System.Windows.Controls.Border
            {
                Child = grid,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 3,
                    ShadowDepth = 2,
                    Opacity = 0.5
                }
            };

            return border;
        }

        /// <summary>
        /// 複数選択時の視覚効果を作成
        /// </summary>
        private UIElement CreateMultipleVisual(int count)
        {
            var grid = new System.Windows.Controls.Grid();

            // 重なり合った枠を作成
            for (int i = 2; i >= 0; i--)
            {
                var border = new System.Windows.Controls.Border
                {
                    Width = 80,
                    Height = 80,
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Background = Brushes.White,
                    Margin = new Thickness(i * 5, i * 5, 0, 0)
                };
                grid.Children.Add(border);
            }

            // ファイル数を表示
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = $"{count}個のファイル",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10)
            };
            grid.Children.Add(textBlock);

            var outerBorder = new System.Windows.Controls.Border
            {
                Child = grid,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 3,
                    ShadowDepth = 2,
                    Opacity = 0.5
                }
            };

            return outerBorder;
        }

        /// <summary>
        /// ドラッグ効果を取得します
        /// </summary>
        public DragDropEffects GetDragEffects(DragEventArgs e)
        {
            // Ctrlキーが押されている場合はコピー
            if ((e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey)
            {
                return DragDropEffects.Copy;
            }

            // Shiftキーが押されている場合は移動
            if ((e.KeyStates & DragDropKeyStates.ShiftKey) == DragDropKeyStates.ShiftKey)
            {
                return DragDropEffects.Move;
            }

            // デフォルトは移動
            return DragDropEffects.Move;
        }
    }
}
