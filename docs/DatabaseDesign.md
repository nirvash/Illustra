# データベースアクセス層の設計

## 概要

データベースアクセス層を 2 つのレイヤーに分離し、それぞれの責務を明確にする。

## レイヤー構成

### DatabaseAccess (低レベル)

- 基本的なデータベース操作の実行
- 同時実行制御
- トランザクション管理
- エラー処理とリトライロジック
- ロギング

非同期処理の実装:

```csharp
public async Task<T> ReadAsync<T>(Func<DataConnection, Task<T>> operation, CancellationToken cancellationToken = default)
{
    return await Task.Run(async () =>
    {
        await _readSemaphore.WaitAsync(cancellationToken);
        try
        {
            // データベース操作の実行
            // ...
        }
        finally
        {
            _readSemaphore.Release();
        }
    }, cancellationToken);
}
```

### DatabaseManager (高レベル)

- ビジネスロジック
- 進捗報告
- キャンセル処理
- 高レベルなトランザクション管理

例：クリーンアップ処理の実装:

```csharp
public async Task<(int, int)> CleanupDatabaseAsync(Action<string, double> progressCallback, CancellationToken cancellationToken)
{
    // 進捗報告とキャンセル処理を管理
    await _dbAccess.WriteAsync(async db =>
    {
        // データベース操作
        // 進捗報告
        // キャンセル確認
    });
}
```

## 改善点

1. 非同期処理の一貫性

   - DatabaseAccess レベルで`Task.Run`を使用して別スレッドでの非同期実行を保証
   - UI スレッドのブロッキングを防止
   - すべてのデータベース操作が確実に別スレッドで行われることをコード構造で明示

2. スレッド分離の強化

   - データベース操作が UI スレッドを占有せず、UI の応答性を維持
   - 各データベースメソッド（ReadAsync, WriteAsync, WriteWithResultAsync, WriteWithTransactionAsync）
     のすべてが一貫して別スレッドで実行される構造
   - 大量のデータ処理時でも UI のフリーズを防止

3. 責務の分離

   - DatabaseAccess: データベース操作の基本機能とスレッド管理を担当
   - DatabaseManager: アプリケーション固有のロジックと進捗報告を担当

4. エラーハンドリング

   - DatabaseAccess: 基本的なデータベースエラーとリトライロジック
   - DatabaseManager: ビジネスロジックに関連するエラー処理

5. トランザクション管理

   - 明示的なトランザクション境界を設定
   - ACID 特性の保証
   - 一貫したトランザクション実装

6. キャンセル処理の統合
   - CancellationToken の一貫した伝播
   - 長時間実行操作のタイムアウト処理
   - ユーザーによるキャンセルのサポート

## 実装方針

1. DatabaseAccess の修正

   - すべてのデータベースメソッドで`Task.Run`を使用した実装
   - 適切なスレッド管理とキャンセレーショントークンの伝播
   - ログ機能強化によるデバッグサポート
   - 同時実行制御の改善

2. DatabaseManager の修正
   - 進捗報告と UI スレッドとの連携の強化
   - キャンセル処理の統合
   - エラーハンドリングの強化
   - 高レベル API の改善と使いやすさの向上
