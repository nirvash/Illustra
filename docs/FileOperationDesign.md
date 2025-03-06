# ファイル操作機能の設計

[Previous content up to the "## まとめ" section remains unchanged...]

## 次フェーズ：FileOperationHelperの実装計画

### 1. クラス設計

```csharp
public class FileOperationHelper
{
    private readonly DatabaseManager _db;

    public FileOperationHelper(DatabaseManager db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task MoveFile(string source, string dest)
    {
        // レーティング情報の取得
        var sourceNode = await _db.GetFileNodeAsync(source);
        var rating = sourceNode?.Rating ?? 0;

        // ファイル移動
        File.Move(source, dest);

        // 新しいノードを作成
        var newNode = new FileNodeModel(dest)
        {
            Rating = rating
        };
        await _db.SaveFileNodeAsync(newNode);

        // 古いノードを削除
        if (sourceNode != null)
        {
            await _db.DeleteFileNodeAsync(source);
        }
    }

    public async Task CopyFile(string source, string dest)
    {
        // レーティング情報の取得
        var sourceNode = await _db.GetFileNodeAsync(source);
        var rating = sourceNode?.Rating ?? 0;

        // ファイルコピー
        File.Copy(source, dest);

        // 新しいノードを作成
        var newNode = new FileNodeModel(dest)
        {
            Rating = rating
        };
        await _db.SaveFileNodeAsync(newNode);
    }

    public async Task DeleteFile(string path)
    {
        // ファイル削除
        File.Delete(path);

        // データベースから削除
        await _db.DeleteFileNodeAsync(path);
    }
}
```

### 2. DIコンテナ設定の更新

```csharp
protected override void RegisterTypes(IContainerRegistry containerRegistry)
{
    containerRegistry.RegisterSingleton<IEventAggregator, EventAggregator>();
    containerRegistry.RegisterSingleton<DatabaseManager>();
    containerRegistry.RegisterTransient<FileOperationHelper>();
}
```

### 3. 移行手順

1. ファイル操作関連のコードの特定
   - ThumbnailLoaderHelperからの移行
   - ThumbnailListControlからの移行

2. 段階的な移行
   ```csharp
   // ThumbnailListControlでの使用例
   public class ThumbnailListControl
   {
       private readonly FileOperationHelper _fileOperationHelper;

       public ThumbnailListControl()
       {
           _fileOperationHelper = ContainerLocator.Container.Resolve<FileOperationHelper>();
       }
   }
   ```

### 4. テスト計画

1. 基本機能テスト
   - ファイルの移動
   - ファイルのコピー
   - ファイルの削除
   - レーティングの引き継ぎ

2. エラーケース
   - ファイルが存在しない
   - 権限エラー
   - DBアクセスエラー

3. 統合テスト
   - ThumbnailLoaderHelperとの連携
   - UIコンポーネントとの連携

### 5. 移行後の検証項目

- [ ] 既存機能が正常に動作することの確認
- [ ] レーティング情報が正しく引き継がれることの確認
- [ ] エラーハンドリングの動作確認
- [ ] パフォーマンスへの影響確認

### 6. 期待される効果

1. **責務の明確化**
   - ファイル操作に特化したクラスの提供
   - コードの可読性向上
   - メンテナンス性の向上

2. **再利用性の向上**
   - 他のコンポーネントからの利用が容易
   - 依存関係の明確化
   - テストの容易性

3. **エラーハンドリングの改善**
   - 一貫したエラー処理
   - トランザクション管理の改善
   - ログ記録の統一

## 実装スケジュール

1. FileOperationHelperクラスの作成
2. 既存コードの移行
3. テストの実装
4. 動作検証
5. 必要に応じた調整

この計画に基づいて、FileOperationHelperの実装を進めていきます。
