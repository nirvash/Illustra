# ファイル操作機能の設計

[Previous content up to "## 注意点" section remains unchanged...]

## リファクタリング実装プラン

### Phase 1: DIコンテナ導入とThumbnailLoaderHelper修正

1. DIコンテナの設定
```csharp
// Startup.cs または App.xaml.cs
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // データベース
        services.AddSingleton<DatabaseManager>();
    }

    // ServiceProviderは内部的にのみ公開
    internal static IServiceProvider? ServiceProvider =>
        (Current as App)?._serviceProvider;
}
```

2. ThumbnailLoaderHelperの修正
```csharp
public class ThumbnailLoaderHelper
{
    private readonly DatabaseManager _db;

    public ThumbnailLoaderHelper(
        ItemsControl thumbnailListBox,
        Action<string> selectCallback,
        ThumbnailListControl control,
        MainViewModel viewModel)
    {
        // 必要なコンポーネントで直接ServiceProviderを使用
        _db = App.ServiceProvider?.GetRequiredService<DatabaseManager>() ??
            throw new InvalidOperationException("DatabaseManager is not registered");

        _thumbnailListBox = thumbnailListBox ?? throw new ArgumentNullException(nameof(thumbnailListBox));
        _selectCallback = selectCallback;
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
```

### メリット
1. シンプルな依存関係
   - 必要なコンポーネントでのみServiceProviderを使用
   - 既存のコンストラクタシグネチャを維持
   - 上位レイヤーへの影響を最小化

2. カプセル化の維持
   - DIコンテナは必要なコンポーネントでのみ使用
   - 他のコンポーネントには依存関係を見せない
   - アプリケーション構造の単純化

3. 段階的な導入
   - 既存コードへの影響を最小限に抑制
   - 必要な箇所から順次対応可能
   - 問題発生時の切り戻しが容易

### 確認項目
1. 機能テスト
   - サムネイル表示
   - レーティング情報の保持
   - ファイル操作

2. エラーケース
   - DIコンテナ未初期化
   - データベースアクセスエラー

### リスク対応
1. 初期化エラー
   - 適切な例外メッセージ
   - エラーログの記録
   - リトライロジックの検討

2. パフォーマンス確認
   - メモリ使用量
   - 初期化時間
   - UI応答性

### 成功基準
- [ ] 既存機能が正常に動作
- [ ] パフォーマンスが維持されている
- [ ] エラー処理が適切に機能
- [ ] メモリリークが発生していない

### 次のステップ
1. エラー処理の改善
2. テストケースの作成
3. パフォーマンス計測と最適化
