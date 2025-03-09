using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using GongSolutions.Wpf.DragDrop;
using Illustra.Models;

namespace Illustra.Helpers
{
    public class FileNodeDragHandler : DefaultDragHandler
    {
        private static readonly string FileNodesFormat = typeof(FileNodeModel).Name;

        public override void StartDrag(IDragInfo dragInfo)
        {
            // 標準のドラッグハンドラ処理を実行（これによりAdornerが表示される）
            base.StartDrag(dragInfo);

            // DataObjectを作成
            var dataObject = new DataObject();
            var filePaths = new StringCollection();
            var filePathsText = string.Empty;

            // ドラッグされている項目からファイルパスを取得
            if (dragInfo.SourceItems != null && dragInfo.SourceItems.Cast<object>().Any())
            {
                // アプリ内のドラッグ＆ドロップ用にSourceItemsを保持
                dataObject.SetData(FileNodesFormat, dragInfo.SourceItems);

                // ファイルパスを収集
                var paths = dragInfo.SourceItems.Cast<object>()
                    .OfType<FileNodeModel>()
                    .Where(file => System.IO.File.Exists(file.FullPath))
                    .Select(file => file.FullPath)
                    .ToList();

                // StringCollectionとテキストを設定
                filePaths.AddRange(paths.ToArray());
                filePathsText = string.Join(Environment.NewLine, paths);
            }
            else if (dragInfo.SourceItem is FileNodeModel fileItem &&
                     System.IO.File.Exists(fileItem.FullPath))
            {
                filePaths.Add(fileItem.FullPath);
                filePathsText = fileItem.FullPath;
            }

            // ファイルリストが空でなければ設定
            if (filePaths.Count > 0)
            {
                // エクスプローラー用のファイルドロップ形式
                dataObject.SetFileDropList(filePaths);

                // メモ帳などのテキスト形式
                if (!string.IsNullOrEmpty(filePathsText))
                {
                    dataObject.SetText(filePathsText);
                }
            }

            // 作成したDataObjectを設定
            dragInfo.DataObject = dataObject;
        }

        public override bool CanStartDrag(IDragInfo dragInfo)
        {
            // FileNodeModelのアイテムのみドラッグを許可
            return dragInfo.SourceItems.Cast<object>().All(item => item is FileNodeModel);
        }
    }
}
