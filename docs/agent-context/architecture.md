# Illustra アーキテクチャ

Windows 用画像ビューア。WPF (.NET 9) + MVVM（Prism 9 / DryIoc / イベントアグリゲーター）。単一アプリ + 単一テストプロジェクトの小さめソリューション。

## 主要コンポーネントと関係

```
Views (WPF) ←→ ViewModels ←→ Models
     ↓ イベントアグリゲーター (Events/) で画面間疎結合通信
Helpers (ロジック本体: DB/サムネイル/ファイル操作/メタデータ解析)
Services (横断サービス: 言語切替、プロパティ、Dispatcher 抽象)
Mcp/ (アプリ内 Kestrel で動く MCP サーバー → UI を経由してアプリ操作)
```

- **DI**: `App.xaml.cs` (`PrismApplication`) が唯一の組立点。`RegisterTypes` でシングルトン登録（`DatabaseManager`, `ThumbnailListViewModel`, `MainWindowViewModel`, `IllustraAppContext`, `McpHostManager` 等）。エントリポイントはここ。
- **イベントアグリゲーター**: 画面間通信の標準手段。定義は `src/Events/UIEvents.cs`（UI）と `McpEvents.cs`（MCP）。
- **共有状態**: `Models/IllustraAppContext.cs` がアプリ全体の共有状態（選択ファイルの `CurrentProperties`、MainViewModel 参照）を保持するシングルトン。

## 主要データフロー

1. フォルダ選択（FileSystemTreeViewModel/FolderTreeControl）
2. → ThumbnailListViewModel が `FileNodeModel` 一覧を構築（DB からレーティング等を合成）
3. → サムネイルは `Helpers/ThumbnailLoaderHelper` + `ThumbnailRequestQueue` で非同期生成、`WindowBasedImageCache` でキャッシュ
4. 選択変更 → `IllustraAppContext.CurrentProperties` 更新 → PropertyPanel に表示（Stable Diffusion プロンプト等のメタデータ含む）
5. Enter で ImageViewerWindow（ズーム/パン/スライドショー）を開く
6. レーティング・設定変更 → SQLite / JSON 設定ファイルへ永続化

## 永続化（2 系統）

| 種類 | 仕組み | 内容 |
|---|---|---|
| 設定 | JSON ファイル（Newtonsoft.Json） | `SettingsHelper.GetSettings()` → `Models/AppSettingsModel` |
| データ | SQLite（linq2db） | ファイルパス紐付けのレーティング等。低レベル `DatabaseAccess` / 高レベル `DatabaseManager` |

## MCP サーバー（src/Mcp/）

- アプリ内 Kestrel ホストで `http://localhost:5149/mcp` を公開（公式 ModelContextProtocol ASP.NET Core SDK）。
- `McpHostService`（ホスト）/ `McpHostManager`（ライフサイクル管理シングルトン）/ `BearerTokenMiddleware` + `McpAccessTokenGenerator`（認証）。
- `Tools/*.cs`（folder/file/selection/metadata/application ツール）→ `McpAppBridge` 経由で Dispatcher 上の UI 状態を操作。
- 外部クライアントからの操作は必ず UI スレッド経由でアプリ状態に反映される。

## 守るべき境界・制約

- ViewModel → Helper の依存は可。View → ViewModel 以外の逆方向依存や、Helper 間の循環を作らない。
- UI 更新は UI スレッド上で（必要なら `IDispatcherService` / `Dispatcher.InvokeAsync`）。
- DB スキーマ変更は `docs/DatabaseDesign.md` の設計に従い `DatabaseAccess`/`DatabaseManager` の責務分界を維持。
- 多言語リソース（Resources/Strings*.xaml）とテーマ（Themes/、MahApps+ControlzEx）は全 View から参照される横断要素。変更時は影響範囲が広い。

## 詳細ドキュメント（必要時のみ）

- 機能↔実装マップ: `docs/Implementation.md`
- 開発ルール: `docs/Rule.md` / DB 設計: `docs/DatabaseDesign.md`
- MCP 設計: `docs/MCP_v2_Design.md` / キャッシュ: `docs/ImageCacheDesign.md` / ズーム&パン: `docs/ZoomDesign.md`
