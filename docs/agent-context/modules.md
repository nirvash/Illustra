# モジュール地図（どこを見るべきか）

「この問題ならどのファイルを読むべきか」を判断するための索引。全ファイル網羅ではない。

## タスク種別 → まず見る場所

| タスク | 最初に見る場所 |
|---|---|
| サムネイル表示・選択・フィルタ・ソート | `ViewModels/ThumbnailListViewModel.cs`, `Views/ThumbnailListControl*` |
| サムネイル生成・キャッシュ性能 | `Helpers/ThumbnailLoaderHelper.cs`, `ThumbnailRequestQueue.cs`, `WindowBasedImageCache.cs` |
| フォルダツリー・お気に入り | `ViewModels/FileSystemTreeViewModel.cs`, `Views/FileSystemTreeView*`, `FolderTreeControl*` |
| 画像ビューア（ズーム/パン/スライドショー） | `Views/ImageViewerWindow.*`, `docs/ZoomDesign.md` |
| メタデータ（Exif/StableDiffusion/ComfyUI/MP4/PNG チャンク） | `Helpers/*Metadata*.cs`, `Helpers/PngTextChunk*.cs`, `Helpers/ComfyUI*.cs` |
| WebP アニメーション / MP4 再生 | `Services/WebpAnimationService.cs`, `ViewModels/WebpPlayerViewModel.cs`, `Helpers/LibWebP.cs` |
| ファイル操作（コピー/移動/削除/D&D） | `Helpers/FileOperationHelper.cs`, `DragDropHelper.cs`, `FileNodeDragHandler.cs` |
| レーティング | `Helpers/RatingHelper.cs` + `Helpers/DatabaseManager.cs` |
| 設定項目の追加 | `Models/AppSettingsModel.cs` + `Helpers/SettingsHelper.cs` |
| 多言語文言の追加・変更 | `Resources/Strings.xaml` + `Strings.ja.xaml`（両方必須）+ `Services/LanguageService.cs` |
| MCP ツール追加 | `Mcp/Tools/*.cs` + `Events/McpEvents.cs`, `docs/MCP_v2_Design.md` |
| DB 関連 | `Helpers/DatabaseAccess.cs`（低レベル）/ `DatabaseManager.cs`（高レベル）、設計は `docs/DatabaseDesign.md` |
| キーボードショートカット | `KeyboardShortcutHandler.cs`（src 直下）, `Views/MainWindow.KeyboardHandler.cs`, `Models/KeyboardShortcut*.cs` |
| テーマ・見た目 | `Themes/`, `App.xaml.cs`（ControlzEx Theming） |

## Views/ (src/Views)

Purpose: XAML + code-behind。Partial クラスで機能分割（例: MainWindow = 本体 + KeyboardHandler + Properties）。
Primary files: `MainWindow.xaml(.cs)`, `ImageViewerWindow.*`, `ThumbnailListControl.*`, `FileSystemTreeView.*`, 各種ダイアログ。
Depends on: ViewModels, Controls, Events。
Usually relevant for: UI レイアウト変更、操作ハンドリング、ダイアログ追加。
NOT relevant for: ビジネスロジック修正（Helpers へ）。ロジックを code-behind に足さないこと。

## ViewModels/ (src/ViewModels)

Purpose: Prism MVVM の ViewModel。
Primary files: `MainWindowViewModel.cs`（タブ管理・設定画面起動・イベント集約）, `ThumbnailListViewModel.cs`（シングルトン本体：一覧/フィルタ/ソート/選択）, `TabViewModel.cs`, `FileSystemTreeViewModel.cs`。
Entry points: `ThumbnailListViewModel` は DI で singleton 登録され IllustraAppContext 経由でも参照される。
Usually relevant for: 一覧操作、タブ、状態管理。
NOT relevant for: 永続化の詳細（DB/Settings の Helper 内側）。

## Helpers/ (src/Helpers) — ロジックの大半

Purpose: 実質的なドメイン層。サブグループ:
- **DB**: `DatabaseAccess.cs`（同時実行制御・トランザクション）/ `DatabaseManager.cs`（ファイル情報・レーティング API）
- **サムネイル**: `ThumbnailLoaderHelper.cs`, `ThumbnailRequestQueue.cs`, `DefaultThumbnailProcessor.cs`, `WindowBasedImageCache.cs`
- **ファイル**: `FileOperationHelper.cs`, `FileHelper.cs`, `FileSystemMonitor.cs`（フォルダ監視）
- **メタデータ解析**: `StableDiffusionMetadataParser.cs`, `ComfyUIMetadataParser.cs`, `ComfyUIGraphAnalyzer.cs`, `Mp4MetadataReader.cs`, `PngTextChunkReader.cs`, `MediaGenerationMetadataParser.cs`
- **設定**: `SettingsHelper.cs` → `Models/AppSettingsModel`
- **WebP**: `LibWebP.cs`, `WebPHelper.cs`（ネイティブ dll P/Invoke）
- UI 補助: `ToastNotificationHelper.cs`, `DialogHelper.cs`, `LogHelper.cs`
Usually relevant for: 上記ドメインの動作変更。単体テストも主にここを対象。
NOT relevant for: 単なる文言・レイアウト変更。

## Services/ (src/Services)

Purpose: 横断サービス。`LanguageService`（言語切替とリソース適用）, `ImagePropertiesService`（プロパティパネル用情報）, `WebpAnimationService`, `IDispatcherService`/`WpfDispatcherService`（テスト可能な Dispatcher 抽象）。
Usually relevant for: 言語切替挙動、プロパティ表示、UI スレッド実行の抽象化。

## Models/ (src/Models)

Purpose: データモデル + 共有コンテキスト。`IllustraAppContext.cs`（アプリ共有状態シングルトン）が最重要。他は `FileNodeModel`（一覧要素・DB 行に対応）、`AppSettingsModel`、各種 Settings モデル。
Usually relevant for: 保持データ構造の変更、新設定項目。

## Mcp/ (src/Mcp)

Purpose: アプリ内 Kestrel MCP サーバー。構成は architecture.md 参照。ツール追加は `Tools/*.cs` に実装し UI 側状態は `McpAppBridge` を経由。
Usually relevant for: MCP ツールの追加・変更のみ。通常のビューア機能には無関係。

## Events/ (src/Events)

Purpose: イベントアグリゲーター用イベント定義。`UIEvents.cs`（UI 全般）、`McpEvents.cs`（MCP）、個別イベントファイル数件。画面間通信を追加するときはここに定義する。

## Resources/, Themes/, Controls/, Converters/

- `Resources/Strings.xaml`（英）+ `Strings.ja.xaml`（日）: 文言。**両方同時に更新必須**。
- `Themes/`: MahApps ベース ライト/ダークテーマ。
- `Controls/`: 再利用カスタムコントロール。`Converters/`: XAML 値コンバーター。

## tests/ (tests/Illustra.Tests.csproj, NUnit + Moq)

Purpose: 主に Helpers のメタデータ解析系ユニットテスト（`Helpers/*Tests.cs`、テスト用 PNG/MP4 ビルダー付き）と MCP ホスト統合テスト（`MCPHost/`、AspNetCore TestHost 使用）。UI/ViewModel のテストはほぼ無い。
Usually relevant for: パーサ系・DB・MCP 変更時。XAML/View 変更では不要。
