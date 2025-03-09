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
        public override void StartDrag(IDragInfo dragInfo)
        {
            // 標準のドラッグハンドラ処理を実行（これによりAdornerが表示される）
            base.StartDrag(dragInfo);

            // ドラッグデータを外部アプリケーション用に拡張
            var dataObject = dragInfo.DataObject as DataObject;
            if (dataObject == null) {
                dataObject = new DataObject();
                dragInfo.DataObject = dataObject;
            }
            if (dataObject != null)
            {
                var filePaths = new StringCollection();

                // ドラッグされている項目からファイルパスを取得
                if (dragInfo.SourceItems != null && dragInfo.SourceItems.Cast<object>().Any())
                {
                    foreach (var item in dragInfo.SourceItems)
                    {
                        if (item is FileNodeModel file && System.IO.File.Exists(file.FullPath))
                        {
                            filePaths.Add(file.FullPath);
                        }
                    }
                }
                else if (dragInfo.SourceItem is FileNodeModel fileItem &&
                         System.IO.File.Exists(fileItem.FullPath))
                {
                    filePaths.Add(fileItem.FullPath);
                }

                // ファイルリストが空でなければ設定（存在するファイルのみ）
                if (filePaths.Count > 0)
                {
                    dataObject.SetFileDropList(filePaths);
                    dataObject.SetText(string.Join(Environment.NewLine, filePaths)); // こちらはテキスト形式なのでメモ帳などに張り付けるときに使われる
                    dataObject.SetData("FileNodeModel", dragInfo.SourceItems);

                    // DataObjectはリファレンス型なので、既存のオブジェクトの内容を
                    // 変更することで更新される（再代入は不要）
                }
            }
        }

        public override bool CanStartDrag(IDragInfo dragInfo)
        {
            // FileNodeModelのアイテムのみドラッグを許可
            return dragInfo.SourceItems.Cast<object>().All(item => item is FileNodeModel);
        }
    }
}
