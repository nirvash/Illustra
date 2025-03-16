# Illustra 開発ルール

## 共通ルール

- 機能と実装箇所のマッピングは `docs/Implementation.md` を参照する。このマッピングはコードの理解と保守性向上のための重要なガイドとなる

## UI 実装ルール

### 多言語対応

- 文言は `Resources/Strings.xaml` に英語文言、`Resources/Strings.ja.xaml` に日本語文言を定義する
- 文言 ID は `String_<カテゴリ>_<名称>` のフォーマットで定義する
- **重要**: コードで参照する文言リソースは必ず両方の言語ファイルに定義すること。定義漏れがあると起動時にクラッシュする
- 新機能実装時は、使用するすべての文言リソースが定義されていることを確認する
- 文言リソースを追加した場合は、必ずビルドして動作確認を行う

```xaml
<!-- 例: アプリケーション名の定義 -->
<system:String x:Key="String_App_Name">Illustra</system:String>
```

### ダイアログデザイン

- ダイアログのボタン配置は、キャンセルボタンを左側、OK ボタンを右側に配置する
- これは日本語環境でも英語環境でも同様とする

```xaml
<!-- 例: ダイアログのボタン配置 -->
<StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
    <Button Content="{DynamicResource String_Common_Cancel}" Margin="0,0,10,0" IsCancel="True"/>
    <Button Content="{DynamicResource String_Common_Ok}" IsDefault="True"/>
</StackPanel>
```

### イベント処理

- 画面間の通信にはイベントアグリゲーターを使用する
- イベント定義は `Events/UIEvents.cs` に追加する

```csharp
// 例: ファイル選択イベントの定義と使用
public class FileSelectedEvent : PubSubEvent<string> { }
// 発行
eventAggregator.GetEvent<FileSelectedEvent>().Publish(filePath);
// 購読
eventAggregator.GetEvent<FileSelectedEvent>().Subscribe(HandleFileSelected);
```

### ViewModel パターン

- ViewModel は `INotifyPropertyChanged` を実装する
- バッキングフィールドは private readonly で定義する

```csharp
// 例: ViewModelでのプロパティ実装
private readonly string _title;
public string Title
{
    get => _title;
    set
    {
        if (_title != value)
        {
            _title = value;
            OnPropertyChanged();
        }
    }
}
```

## データ操作ルール

### 設定管理

- 設定値は `SettingsHelper.GetSettings()` を使用してアクセスする
- 新しい設定は `AppSettings` クラスに定義し、デフォルト値を設定する

```csharp
// 例: 設定の定義と使用
public class AppSettings
{
    public int ThumbnailSize { get; set; } = 200; // デフォルト値: サムネイルサイズ
    public double MouseWheelMultiplier { get; set; } = 1.0; // デフォルト値: マウスホイールスクロール倍率
}
```

### データの永続化

アプリケーションには 2 種類の永続化方法があります：

#### 1. 設定の永続化

- `SettingsHelper` を使用して JSON 形式で保存
- アプリケーション設定の永続化に使用

```csharp
// 永続化される設定の例
public class AppSettings
{
    public int ThumbnailSize { get; set; }  // サムネイルサイズ
    public bool SortByDate { get; set; }    // 日付ソートの有効/無効
    public string LastSelectedPath { get; set; }  // 最後に選択したパス
    public Dictionary<string, string> KeyboardShortcuts { get; set; }  // キーボードショートカット
}
```

#### 2. データベースによる永続化

- `DatabaseManager` を使用して SQLite に保存
- ファイル情報やメタデータの永続化に使用

````csharp
// データベースに永続化される情報の例
public class FileNodeModel
{
    [PrimaryKey]
    public string FullPath { get; set; }     // ファイルの完全パス
    public string FolderPath { get; set; }   // フォルダパス
    public string FileName { get; set; }     // ファイル名
    public DateTime CreationTime { get; set; }  // 作成日時
    public int Rating { get; set; }          // レーティング（★の数）
    public bool HasPrompt { get; set; }      // プロンプト情報の有無
    public DateTime LastModified { get; set; }  // 最終更新日時
}

// データベース永続化の使用例
// 1. 基本的なCRUD操作
await databaseManager.SaveFileNodeAsync(fileNode);         // Create
var node = await databaseManager.GetFileNodeAsync(path);  // Read
await databaseManager.UpdateRatingAsync(path, rating);    // Update
await databaseManager.DeleteFileNodeAsync(path);          // Delete

// 2. バッチ処理（大量データの永続化）
await databaseManager.SaveFileNodesBatchAsync(fileNodes);

// 3. トランザクションを使用した永続化
await databaseManager.WriteWithTransactionAsync(async db => {
    await db.DeleteAsync(oldNodes);  // 古いデータを削除
    await db.InsertAsync(newNodes);  // 新しいデータを追加
});

// データベースに永続化するデータの使い分け：
// - ファイルのメタ情報（パス、作成日時、更新日時）
// - ユーザー付与情報（レーティング、タグ）
// - ファイル解析情報（プロンプト情報の有無、画像サイズ）

// 永続化しないデータの例：
// - 一時的なUI状態
// - キャッシュされたサムネイル画像
// - 実行時の選択状態
```csharp
// 例: データモデルの定義
public class FileNodeModel
{
    [PrimaryKey]
    public string FullPath { get; set; }
    public string FolderPath { get; set; }
    public int Rating { get; set; }
}

// 例: データの保存と取得
await databaseManager.SaveFileNodeAsync(fileNode);  // 単一エントリの保存
var nodes = await databaseManager.GetFileNodesAsync(folderPath);  // データの取得
await databaseManager.UpdateRatingAsync(fullPath, rating);  // データの更新
````

- 大量データの永続化には専用メソッドを使用する

```csharp
// 例: バッチ処理による永続化
await databaseManager.SaveFileNodesBatchAsync(fileNodes);
```

- トランザクションが必要な場合は専用メソッドを使用する

```csharp
// 例: トランザクションを使用した永続化
await databaseManager.WriteWithTransactionAsync(async db => {
    await db.DeleteAsync(oldNodes);
    await db.InsertAsync(newNodes);
});
```

### ファイル操作

- ファイル操作は `FileOperationHelper` クラスのメソッドを使用する
- 直接の File I/O 操作は避ける

```csharp
// 例: ファイル操作の実装
await fileOperationHelper.DeleteFile(filePath);  // 推奨
// File.Delete(filePath);  // 非推奨
```

## 非同期処理ルール

### 基本原則

- 非同期処理は async/await を使用する
- UI 操作を含む場合は `App.Current.Dispatcher.InvokeAsync` を使用する

```csharp
// 例: UI更新を含む非同期処理
await App.Current.Dispatcher.InvokeAsync(() => {
    statusText.Text = "処理完了";
});
```

### コレクション操作

- 変更通知が必要な場合は `ObservableCollection` を使用する
- 大量更新時は `BulkObservableCollection` を使用する

```csharp
// 例: コレクションの使い分け
public ObservableCollection<FileItem> Files { get; } = new();  // 通常の更新
public BulkObservableCollection<FileItem> CacheItems { get; } = new();  // 大量更新
```

### データバインディング

- フィルタリングは `ICollectionView` を使用する
- ビューは `CollectionViewSource.GetDefaultView` で取得する

```csharp
// 例: フィルタリングの実装
var view = CollectionViewSource.GetDefaultView(Items);
view.Filter = item => ((FileItem)item).Rating > 3;
```

## システム連携ルール

### ファイルシステム監視

- `FileSystemWatcher` を使用してファイル変更を監視する
- 変更イベントは `FileSystemMonitor` クラスで集約する

```csharp
// 例: ファイル監視の実装
fileSystemMonitor.StartMonitoring(folderPath);
fileSystemMonitor.FileChanged += HandleFileChanged;
```

### キーボード操作

- ショートカット定義は `KeyboardShortcutSettings` で管理する
- 既存の定義スタイルに従う

```csharp
// 例: ショートカットの定義
Shortcuts.Add(new KeyboardShortcut(Key.O, ModifierKeys.Control, "OpenFile"));
```

## トラブルシューティング

### よくある問題と解決方法

1. UI 更新が反映されない

   - `OnPropertyChanged` の呼び出しを確認
   - UI スレッドでの実行を確認

2. イベントが発行されない

   - イベントの購読が解除されていないか確認
   - 正しいイベントアグリゲーターを使用しているか確認

3. ファイル操作でエラーが発生する
   - `FileOperationHelper` の使用を確認
   - 例外処理の実装を確認

### ビルドエラーの確認と修正

実装後は必ずビルドを実行して、エラーがないことを確認してください。

- 実装が完了したら、必ず `dotnet build` コマンドを実行してビルドエラーをチェックする
- コンパイルエラーや警告が表示された場合は、すぐに修正する
- 特に複数のファイルを修正した場合は、相互依存関係によるエラーに注意する

### 一般的なエラーと修正例

1. **名前空間の不足**

   - エラー: `The type or namespace name 'X' could not be found`
   - 修正: 必要な `using` ディレクティブを追加する

2. **プロパティの重複定義**

   - エラー: `The type 'X' already contains a definition for 'Y'`
   - 修正: 重複している定義を削除するか、partial クラスを適切に分割する

3. **引数の型の不一致**

   - エラー: `Cannot convert from 'X' to 'Y'`
   - 修正: 適切な型変換を行うか、正しい型の引数を使用する

4. **XAML コンポーネント参照エラー**
   - エラー: `The name 'X' does not exist in the current context`
   - 対応: これはリンターエラーであり、ビルド時には問題ありません。コメントで説明を追加する

### ビルドコマンドの実行

実装後は以下のコマンドを実行してビルドエラーをチェックしてください：

```bash
dotnet build
```

このコマンドはプロジェクトをビルドし、エラーや警告を表示します。エラーが表示された場合は、すぐに修正してください。
