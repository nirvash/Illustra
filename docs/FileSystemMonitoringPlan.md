# フォルダリスト更新監視機能の実装計画

## 実装フェーズ

### フェーズ1：フォルダ変更の検知機能実装

1. FileSystemTreeViewModelの拡張
```csharp
public class FileSystemTreeViewModel : INotifyPropertyChanged, IFileSystemChangeHandler
{
    private readonly FileSystemMonitor _monitor;

    public FileSystemTreeViewModel()
    {
        _monitor = new FileSystemMonitor(this);
    }

    // IFileSystemChangeHandler実装
    public void OnFileCreated(string path)
    {
        if (Directory.Exists(path))
        {
            Debug.WriteLine($"フォルダ作成を検知: {path}");
        }
    }

    public void OnFileDeleted(string path)
    {
        Debug.WriteLine($"フォルダ削除を検知: {path}");
    }

    public void OnFileRenamed(string oldPath, string newPath)
    {
        Debug.WriteLine($"フォルダ名変更を検知: {oldPath} -> {newPath}");
    }
}
```

2. 変更検知のテスト
   - 各フォルダ操作（作成/削除/リネーム）が正しく検知されることを確認
   - 検知のタイミングが適切であることを確認
   - エラーケース（アクセス権限なし等）の確認

### フェーズ2：検知した変更の反映機能実装

1. FileSystemTreeModelの拡張
```csharp
public class FileSystemTreeModel
{
    // フォルダ更新
    public void UpdateFolder(string path)
    {
        var parentPath = Path.GetDirectoryName(path);
        var parentItem = FindItemByPath(parentPath);
        if (parentItem != null)
        {
            LoadSubFolders(parentItem);
        }
    }

    // フォルダ削除
    public void RemoveFolder(string path)
    {
        var item = FindItemByPath(path);
        if (item != null)
        {
            var parent = FindParentItem(item);
            if (parent != null)
            {
                parent.Children.Remove(item);
                if (item.IsSelected)
                {
                    ClearSelection();
                }
            }
        }
    }

    // フォルダ名変更
    public void RenameFolder(string oldPath, string newPath)
    {
        var item = FindItemByPath(oldPath);
        if (item != null)
        {
            item.Name = Path.GetFileName(newPath);
            item.FullPath = newPath;
        }
    }
}
```

2. FileSystemTreeViewModelの拡張（フェーズ1のデバッグコードを実装に置き換え）
```csharp
public class FileSystemTreeViewModel
{
    public void OnFileCreated(string path)
    {
        if (Directory.Exists(path))
        {
            _model.UpdateFolder(path);
        }
    }

    public void OnFileDeleted(string path)
    {
        _model.RemoveFolder(path);
    }

    public void OnFileRenamed(string oldPath, string newPath)
    {
        _model.RenameFolder(oldPath, newPath);
    }
}
```

3. 変更反映のテスト
   - フォルダ作成時のツリー更新確認
   - フォルダ削除時のツリー更新と選択解除確認
   - フォルダ名変更時のツリー更新確認
   - 親フォルダ操作時の子フォルダの処理確認
   - 複数の同時変更の処理確認

## 期待される結果

### フェーズ1完了時
- フォルダの作成/削除/リネームを正しく検知できる
- 検知した変更をデバッグログで確認できる
- エラーケースを適切に処理できる

### フェーズ2完了時
- 検知した変更がツリービューに自動的に反映される
- フォルダ削除時に選択が適切に解除される
- ユーザーの操作を妨げることなく更新が行われる

## 想定されるリスクと対策

1. パフォーマンス
   - 多数のファイル変更が同時に発生した場合の対策
   - 更新処理の最適化（不必要な更新の防止）

2. 信頼性
   - ファイルシステムの権限エラーへの対応
   - 予期しない変更パターンへの対応

3. UI応答性
   - 更新処理中のUI凍結防止
   - 変更通知の適切なスロットリング

## 次のステップ

1. フェーズ1の実装を開始
2. テストを実施し、変更検知が正しく機能することを確認
3. フェーズ2の実装に移行するか判断
