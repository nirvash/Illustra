# MCP v2 設計（公式 SDK 移行版）

## 1. 概要

Illustra に Model Context Protocol (MCP) サーバー機能を提供する。旧実装（独自 HTTP+SSE 方式）を全廃し、**公式 C# SDK（`ModelContextProtocol.AspNetCore`）** に移行する。

| 項目 | 内容 |
|---|---|
| トランスポート | Streamable HTTP のみ（SSE はレガシー扱いのため非対応） |
| エンドポイント | `http://localhost:{port}/mcp`（Kestrel、アプリ内ホスト） |
| 認証 | Bearer トークン（設定に保存、Developer 設定で確認可能） |
| プロトコル | MCP 公式仕様（JSON-RPC 2.0）。Claude Desktop / Cursor / VS Code 等の標準クライアントから直接接続可 |

## 2. 旧実装からの変更点

### 廃止
- `src/MCPHost/Controllers/McpController.cs`、`APIService.cs`、`Startup.cs`、`Models/InvokeRequest.cs`
- 独自エンドポイント `/start` `/invoke` `/events`、静的 SSE 接続管理
- `src/Shared/Attributes/McpToolAttribute.cs`、`IToolExecutor`、`IMcpToolDefinition`、手書き JSON Schema
- `tests/MCPHost/APIServiceTests.cs`、`McpControllerTests.cs`、`SwaggerTests.cs`
- 旧ドキュメント: `docs/mcp_http_spec.md`、`docs/mcp_tool_definition_and_call.md`、`docs/MCP_Refactoring_Plan.md`

### 継承（設計思想）
- **アプリ内 Kestrel ホスト**: WPF プロセス内で ASP.NET Core を起動し、DI コンテナへ `IEventAggregator` を共有（App.xaml.cs の既存構造を踏襲）
- **EventAggregator + TaskCompletionSource ブリッジ**: ツール実行 → UI 側ハンドラで実行 → TCS で結果返却。v1 の「結果がクライアントに届かない」欠陥を SDK の CallToolResult 返却で解消

## 3. アーキテクチャ

```
Claude Desktop / Cursor / VS Code / Cline 等
        │  Streamable HTTP (POST/GET http://localhost:{port}/mcp)
        │  Authorization: Bearer {token}
        ▼
┌─ Illustra.exe ────────────────────────────────────────┐
│  Kestrel (ASP.NET Core, in-process)                   │
│    ├─ BearerTokenMiddleware (401 on missing token)    │
│    └─ MapMcp() ← ModelContextProtocol.AspNetCore      │
│           │                                           │
│   [McpServerTool] 属性付きツール群                      │
│           │                                           │
│   IMcpAppBridge (EventAggregator + TCS)               │
│     ├─ UI スレッド必須の操作: Publish → UIThread 購読    │
│     │   ハンドラ実行 → Tcs.SetResult                    │
│     └─ UI スレッド不要の操作: 直接呼び出し               │
│         (DatabaseManager / LoadFromFileAsync /        │
│          ThumbnailHelper / FileOperationHelper)       │
└───────────────────────────────────────────────────────┘
```

### パッケージ

```xml
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="*(最新安定版)" />
```

- `ModelContextProtocol.AspNetCore` → `ModelContextProtocol` → `ModelContextProtocol.Core` を推移的に取得
- ツール定義は `[McpServerTool]` 属性 + `[Description]` による属性ベース検出（`WithToolsFromAssembly()`）

### ホスト起動（App.xaml.cs 変更）

既存の `ConfigureWebHostDefaults` 構造を維持しつつ Startup を簡素化:

```csharp
// Program 相当（Minimal Hosting）
builder.Services.AddMcpServer(options => {
        options.ServerName = "Illustra";
        options.ServerVersion = version;
    })
    .WithToolsFromAssembly(); // メインアセンブリ内の [McpServerTool] を検出

app.MapMcp(); // /mcp エンドポイント（Streamable HTTP）
```

- ポート: `AppSettingsModel.McpPort`（既定 5149）
- バインド: `http://localhost:{port}` のみ（外部インターフェース不可）
- **開発版 (Debug ビルド) はポートに `DebugPortOffset`(+10) を加算**（`McpHostService` 内 `#if DEBUG`）。インストール済みリリース版と settings.json を共有しても並行起動時に競合しない（例: 設定 5149 → リリース 5149 / 開発 5159）
- Swagger/OpenAPI は廃止（MCP 自身が tools/list を提供）

### Bearer トークン認証

- トークン: 初回有効化時に `McpAccessTokenGenerator` が暗号学的乱数（`RandomNumberGenerator.GetBytes(32)` の Base64Url エンコード）で生成、`AppSettingsModel.McpAccessToken` に永続化
- 検証: `/mcp` への全リクエストに対して `Authorization: Bearer {token}` を要求。不一致・未提示は 401
- 検証時はリクエスト毎に現在の設定値を参照するため、設定画面でのトークン再生成が即時反映される
- クライアント設定例（VS Code mcp.json）:

```json
{
  "servers": {
    "Illustra": {
      "type": "http",
      "url": "http://localhost:5149/mcp",
      "headers": { "Authorization": "Bearer <token>" }
    }
  }
}
```

## 4. シナリオ → ツール対応

| シナリオ | 使用ツール |
|---|---|
| 1. 開発中アプリの正常終了（状態永続化） | `shutdown_application` |
| 2. レーティングに応じたフォルダ分類 | `list_files`(rating 込み) → `create_folder` → `move_files` |
| 3. 生成プロンプトの参照 | `get_current_folder` → `list_files` → `get_file_metadata` |
| 4. 画像内容の視覚把握 | `get_selected_files` → `get_thumbnail` |
| 5. 画像の検索・選択誘導 | `list_files` + `get_thumbnail` → `select_file` |
| 6. お気に入りフォルダの把握 | `get_favorite_folders` |

## 5. ツール仕様（v1: 15 ツール + 拡張 3）

| # | ツール | 引数 | 戻り値 | 実装経路 |
|---|---|---|---|---|
| 1 | `shutdown_application` | なし | `{success}` | Dispatcher で `Application.Current.Shutdown()` → OnExit の通常終了処理が走り状態永続化 |
| 2 | `open_folder` | `folderPath`(必須), `selectedFilePath?` | `{success, folderPath}` | 既存 `McpOpenFolderEvent` + TCS |
| 3 | `select_file` | `paths[]`(必須) | `{selectedCount, paths}` | 新規 `McpSelectFilesEvent` + TCS → ThumbnailListControl が選択反映 |
| 4 | `list_files` | `offset?=0`, `limit?=1000`, `filter?{ratingMin?, ratingMax?, fileType?}` | `{folderPath, totalCount, files:[{path,fileName,fileSize,lastModified,rating}]}` | 新規 `McpGetFileListEvent` + TCS（アクティブタブの読み込み済み一覧スナップショット） |
| 5 | `get_selected_files` | なし | `{files:[{path,fileName}]}` | 新規 `McpGetSelectedFilesEvent` + TCS |
| 6 | `get_app_status` | なし | `{currentFolder, loadedFileCount, selectedFiles:[{path,fileName}], openTabs[], filterState:{ratingMin, promptFilterEnabled, tagFilterEnabled, tagFilters, extensionFilterEnabled, extensionFilters}}` | 新規 `McpGetAppStatusEvent` + TCS（アクティブタブと全タブのフォルダ・選択状態・アクティブビューのフィルタ状態＝ユーザーが見えている状態を返す接続診断/状況把握用） |
| 7 | `get_file_metadata` | `filePath`(必須) | `{basicInfo, rating, userComment, generationMetadata}` | UI スレッド不要: `ImagePropertiesModel.LoadFromFileAsync` + `DatabaseManager.GetFileNodeAsync` |
| 8 | `get_thumbnail` | `filePath`(必須), `maxSize?=512` | ImageContent (base64 JPEG) | UI スレッド不要: `ThumbnailHelper.CreateThumbnailAsync` → JPEG エンコード |
| 9 | `get_favorite_folders` | なし | `{folders:[{path,displayName}]}` | `SettingsHelper.GetSettings().FavoriteFolders` を読み取り |
| 10 | `move_files` | `paths[]`(必須), `targetFolder`(必須) | `{processed:[destPaths], processedCount, requestedCount, failedCount}` | `FileOperationHelper.ExecuteFileOperation(isCopy:false)` — DB レーティング引き継ぎ込み |
| 11 | `copy_files` | 同上 | 同上 | 同上 (`isCopy:true`) |
| 12 | `delete_files` | `paths[]`(必須), `permanent?=false` | 同上（processed=削除完了パス） | `FileOperationHelper.DeleteFileQuietAsync` — 既定で**ごみ箱へ移動**（`permanent:true` で完全削除）。DB エントリ（レーティング等）も削除 |
| 13 | `rename_file` | `filePath`(必須), `newFileName`(必須・パス区切り不可) | `{renamed, oldPath, newPath}` | `FileOperationHelper.RenameFile` — DB 追従。同名ファイル存在時はエラー |
| 14 | `create_folder` | `path`(必須) | `{created, path}` | `Directory.CreateDirectory`（既存なら created=false） |
| 15 | `get_server_info` | なし | `{serverName, version, currentFolder, enabledToolsCount}` | 接続診断用 |
| 16 | `set_files_rating` | `paths[]`(必須), `rating`(必須・0〜5、0で解除) | `{processed:[paths], processedCount, requestedCount, rating, failedCount, failed?}` | UI スレッド不要: `DatabaseManager.UpdateRatingAsync`（Upsert/未登録なら新規ノード）+ `RatingChangedEvent` 発行で表示中ビューへ即時反映 |
| 17 | `set_view_filter` | `promptFilter?`, `ratingMin?`(0〜5), `extensions?[]`, `clear?`=false | `{applied, filterState:{ratingMin, promptFilterEnabled, tagFilterEnabled, tagFilters, extensionFilterEnabled, extensionFilters}}` | 新規 `McpSetViewFilterEvent` + TCS → ThumbnailListControl が既存 `FilterChangedEvent` フローへ橋渡し。UI 表示・ViewModel も連動 |
| 18 | `rename_folder` | `folderPath`(必須), `newFolderName`(必須・パス区切り不可) | `{renamed, oldPath, newPath, databaseUpdated}` | UI スレッド不要: `Directory.Move` + `DatabaseManager.UpdateFolderPathsAsync`（子孫ファイルの FolderPath/FullPath 一括更新）。UI 反映は FileSystemMonitor 経由（監視外ドライブではタブ再オープン時に反映）。DB 更新失敗時は `databaseUpdated:false` を返す |

### 制約・方針

- `move_files` / `copy_files`: 移動先フォルダは**存在必須**（不存在ならエラー。事前に `create_folder` を使用）。同名ファイルは既存ロジックにより `(n)` 付きで退避
- 移動後の一覧更新: アクティブタブフォルダは `FileSystemMonitor`（500ms デバウンス）が自動反映。監視外フォルダへの移動では該当タブ再オープン時に再読込
- `list_files` はアクティブタブの**読み込み済み**ファイルが対象（フィルタ適用後の一覧ではない全量。将来フィルタ反映版を別ツール化検討）
- `get_file_metadata` の `generationMetadata`: ComfyUI/A1111 の prompt/negativePrompt/model/loras/parameters + `RawWorkflowJson` 有無フラグ（巨大 JSON は含めない）
- 全ツールは失敗時に `isError=true` の CallToolResult と人間可読なメッセージを返す（例外をそのまま投げない）

## 6. ブリッジ設計（IMcpAppBridge）

```csharp
public interface IMcpAppBridge
{
    // UI スレッドでアクションを実行し完了を待機（TCS パターンの共通化）
    Task<T> InvokeOnUiThreadAsync<T>(Func<T> action);
    // EventAggregator 経由でリクエスト発行し応答待機
    Task<T> PublishAndWaitAsync<TReq, T>(TReq args) where TReq : McpRequestEventArgs;
}

public abstract class McpRequestEventArgs : EventArgs
{
    public string SourceId { get; set; } = "mcp-v2";
    public TaskCompletionSource<object?>? Completion { get; set; }
}
```

- 新規イベント: `McpSelectFilesEvent`、`McpGetFileListEvent`、`McpGetSelectedFilesEvent`、`McpShutdownEvent`、`McpSetViewFilterEvent`（`Events/McpEvents.cs` に集約）
- 購読側ハンドラは `ThreadOption.UIThread` で購読し、`finally` で必ず `Completion.SetResult(...)`（タイムアウト付き: 既定 30 秒）
- `SourceId` フィルタで自己発火ループを防止（既存パターン踏襲）

## 7. 設定（AppSettingsModel 追加項目）

| プロパティ | 型 | 既定値 | 説明 |
|---|---|---|---|
| `EnableMcpHost` | bool | false | （既存）MCP サーバー有効化 |
| `McpPort` | int | 5149 | リッスンポート |
| `McpAccessToken` | string | "" | Bearer トークン（有効化時に自動生成） |

Developer 設定ウィンドウに独立した「MCP」セクションを追加:
- 有効/無効トグル: **即時反映**（Kestrel の動的な開始/停止。失敗時はエラー表示して元の状態へ復帰）
- ポート番号入力＋「適用」ボタン: 保存後、稼働中ならホストを再起動して反映
- ステータス表示: 実行中なら実効 URL（Debug ビルドはオフセット適用後）を表示
- トークン表示（マスク切替可・コピー可）＋ 再生成ボタン（再生成はリクエスト毎のライブ参照により再起動不要で即反映）
- ライフサイクル管理は `McpHostManager`（DI コンテナ登録シングルトン）に集約。起動時の自動開始も同経由

## 8. 実装構成

```
src/Mcp/
  ├─ McpHostService.cs              (WebApplication 構築: DI/ミドルウェア/MapMcp)
  ├─ McpHostManager.cs              (起動/停止ライフサイクル管理シングルトン、動的開始停止)
  ├─ BearerTokenMiddleware.cs       (/mcp への Bearer トークン検証)
  ├─ McpAccessTokenGenerator.cs     (暗号学的乱数による Bearer トークン生成)
  ├─ McpAppBridge.cs                (IMcpAppBridge: EventAggregator + TCS ブリッジ)
  └─ Tools/
      ├─ ApplicationTools.cs        (shutdown_application, get_app_status)
  ├─ ViewTools.cs               (set_view_filter)
      ├─ FolderTools.cs             (open_folder, get_favorite_folders, create_folder, get_server_info)
      ├─ FileSelectionTools.cs      (select_file, get_selected_files, list_files)
      ├─ MetadataTools.cs           (get_file_metadata, get_thumbnail)
      ├─ RatingTools.cs             (set_files_rating)
      └─ FileOperationTools.cs      (move_files, copy_files, delete_files, rename_file)
src/
  ├─ Events/McpEvents.cs            (Mcp* イベント定義)
  ├─ App.xaml.cs                    (起動時自動開始、OnExit の StopAsync ベース正常停止)
  ├─ Views/ThumbnailListControl.Mcp.cs (選択・一覧・選択取得ハンドラ)
  └─ ViewModels/Settings/McpSettingsViewModel.cs (+UI)
```

## 9. テスト計画（NUnit、tests/Illustra.Tests.csproj 内）

| 対象 | 方法 |
|---|---|
| BearerTokenMiddleware | TestServer でトークン無し/誤り→401、正→通過 |
| ツール引数検証 | 不正引数で isError 結果、例外非送出を確認 |
| move/copy ロジック | テスト用一時フォルダで DB 更新（rating 引き継ぎ）・同名リネームを検証 |
| create_folder | 新規作成/既存冪等性 |
| list_files フィルタ | ratingMin/ratingMax/fileType の絞り込み |
| E2E（任意） | TestServer + MCP SDK Client で initialize→tools/list→tools/call |

## 10. 実装フェーズ

1. **Phase 1**: 旧コード削除、SDK 導入、ホスト+認証+`get_server_info` のみで接続確認（MCP Inspector）
2. **Phase 2**: 状態照会系ツール（list_files / get_selected_files / get_file_metadata / get_thumbnail / get_favorite_folders）
3. **Phase 3**: 操作系ツール（select_file / open_folder / shutdown_application）
4. **Phase 4**: ファイル操作系（create_folder / move_files / copy_files）、設定 UI、ドキュメント整備

各フェーズでビルド警告ゼロ・テスト緑を維持する。
