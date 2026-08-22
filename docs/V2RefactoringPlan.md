# Illustra v2 リファクタリング計画

## 1. 目的

v1.x コードベースは LLM による積み上げ方式で構築された経緯があり、全体設計が存在しないため機能追加が困難になっている。本計画はコードベースを段階的に再構築し、以下の 3 つの目標を達成することを目的とする。

### ゴール

1. **拡張可能なコードベースへの転換**: MCP による外部操作（`docs/MCP_Design.md` 参照）をはじめとする機能追加を「既存 UI の内部に手を入れずに」実装できる構造にする。アプリケーション機能がサービス層の interface として公開され、UI（WPF ViewModels）と外部 API（MCPHost）の両方から同一の入口を呼べる状態を目指す。
   - **想定 MCP クライアント（2 種）**:
     - **開発エージェント**: 開発中の AI コーディングエージェントが動作確認・テストに利用するクライアント。状態照会・操作実行・検証用ツールを必要とし、自動化ループ（テスト→結果確認）で頻繁に呼ぶことを前提とする
     - **ローカル LLM**: ChatGPT デスクトップ等、ユーザーのローカル環境から日常操作に利用するクライアント。フォルダ閲覧・レーティング・メタデータ取得などユーザー向け操作ツールを必要とする
   - このため MCP ツール定義は**外部契約**として扱い、安定化（バージョニング・契約テスト）が必要。§1 ゴール 3 の自動テストはこの契約の保護も担う
2. **設計不備起因の不具合の解消**: Issue #48（タブのルート飛び）で顕在化したような、競合・過渡状態・静的結合に起因する不具合クラスを構造的に排除する。
3. **自動テストの整備**: 主要機能がユニットテストで保護され、CI（GitHub Actions `build.yml` の `dotnet test`）で自動実行される状態にする。目安として Application Services 層と ViewModels の主要分岐を行カバレッジ 70% 以上で覆盖し、不具合修正時には必ず再発防止テストを追加する運用を確立する。

### 非ゴール

- UI/UX の再設計（見た目・操作系は現状維持）
- フレームワーク変更（WPF / .NET 9 / Prism は維持）
- 一括置換（フルスクラッチ）— §5 の判断ゲートで改めて評価するのみ

## 2. 現状分析（2026-08-22 測定）

| 指標 | 値 | 評価 |
|---|---|---|
| 総規模 | C# 32,561 行 / 149 ファイル、XAML 5,836 行 / 38 ファイル | 中規模 |
| Views コードビハインド | 9,737 行（**30%**） | ロジックが View 層に存在（MVVM 逆転） |
| Helpers | 9,432 行（29%）/ static クラス 23 個 | 静的ユーティリティ塊。テスト不能 |
| ViewModels | 4,370 行（13%） | 薄い。View へロジックが漏れている |
| Models | 4,058 行（12%） | 監視ハンドラ等の責務が肥大 |
| Services | **803 行（2.5%）** | サービス層がほぼ不在 |
| Controls | 2,035 行 | |

### ホットスポット（上位ファイル）

| ファイル | 行数 | 問題 |
|---|---|---|
| `Views/ThumbnailListControl.xaml.cs` | **3,525** | 全体の 11%が単一ファイル。監視・並び替え・D&D・クリップボード・選択が混在 |
| `Views/MainWindow.xaml.cs` | 1,323 | partial 分割されているが責務境界が曖昧 |
| `Helpers/ThumbnailLoaderHelper.cs` | 1,250 | static。キャッシュ・キュー・デコードが直結 |
| `Views/ImageViewerWindow.xaml.cs` | 1,248 | |
| `ViewModels/WebpPlayerViewModel.cs` | 937 | |
| `Controls/VideoPlayerControl.xaml.cs` | 904 | |

上位 10 ファイルで全体の約 40%を占める。**問題は分散しておらず集中している**ため、ピンポイント解体が有効。

### テスト状況

- テストコード 721 行 / 10 テスト（ソース比約 2%）
- ホットスポット（ThumbnailListControl 等）への覆盖ゼロ
- 既知の事前失敗 2 件（MCPHost 系、Moq ログアサーション）

## 3. 設計不備のカタログ

実際に発生・観測された不具合クラス。リファクタリングはこれらの**再発構造そのもの**を潰すことを狙う。

| # | 不具合クラス | 実例 | 構造的原因 |
|---|---|---|---|
| D1 | 過渡状態からの誤イベント発火 | #48: FS 監視による Children コレクション丸ごと入れ替え → TreeViewItem 再生成 → WPF が一時的に別ノードを選択 → 無条件 publish でタブ移動 | 差分更新なし・選択変更の通知にガードがなかった |
| D2 | デッドフラグ／未結線の防御コード | `_isExpandingPath` が宣言のみで一度も true にならず防御として無意味だった | 初期化責務が不明確。テストがあれば検出できた |
| D3 | 二重初期化・重複監視 | `FileSystemTreeView_Loaded` で ViewModel を 2 回生成し、FS 監視も二重化（診断ログで同一イベント 2 回記録を確認済み） | ライフサイクル管理が View のコードビハインド任せ |
| D4 | 再入によるクラッシュ | 診断ログ開発者モードゲート追加時に、設定 JSON デシリアライズ中の `FolderPath` セッター → ログ → `GetSettings()` の無限再帰で起動クラッシュ | static Helper 同士が相互呼び出し可能で呼び出し順の契約が存在しない |
| D5 | 失敗の不可視化 | LogHelper は Debug.WriteLine のみでファイル出力がなく、#48 のように稀な不具合が追跡不能だった | 横断的関心事（ロギング）がサービス化されていない |
| D6 | View 直アクセス | 7 つの View ファイルが `DatabaseAccess` / `SettingsHelper` を直接参照 | DI 未整備。interface がないので差し替え・モック不可 |

## 4. 方針: Strangler Fig（段階的置換）

フルスクラッチ（仕様のみ残して書き直し）との比較:

| 観点 | A: 段階的リファクタ | B: フルスクラッチ |
|---|---|---|
| 平行運用 | 可（v1.x 継続出荷） | 困難（凍結 or 二重保守） |
| 行動知識の継承 | 維持 | 消失（WPF 特有の罠 — TreeView コンテナ再生成、FileSystemWatcher デバウンス、Dispatcher 優先度 — を全部再学習する羽目になる） |
| 回帰リスク | 小（差分ごとに検証） | 大（新旧比較手段が最初は存在しない） |
| 失敗モード | 中間状態が長引く頓挫 | v2 ブランチ墓場 |

**A を採用する。** ただし「期限のない改善」を防ぐため、各フェーズに完了基準と撤退判断（§7 ゲート）を設ける。B への移行が必要になった場合でも、Phase 0–1 で作るテスト群と更新済み Spec がスクラッチの入力資産になるため投資は無駄にならない。

## 5. ターゲットアーキテクチャ

```
┌─────────────────────────────────────────────────┐
│ 外部クライアント (Cline 等)                        │
└───────────────┬─────────────────────────────────┘
                │ MCP / REST
┌───────────────▼───────────┐   ┌────────────────┐
│ MCPHost (Kestrel)         │   │ WPF Views      │
│ Controllers               │   │ (薄く保つ)      │
└───────────────┬───────────┘   └───────┬────────┘
                │                       │ Bindings
┌───────────────▼───────────────────────▼────────┐
│ Application Services 層 (新設・本体)             │
│  IFolderNavigationService                      │
│  IThumbnailPipelineService                     │
│  IRatingService / IMetadataService ...         │
│  ※ UI と MCP の両方が同じ interface を呼ぶ       │
├─────────────────────────────────────────────────┤
│ Domain / Models                                 │
│  FileSystemTree (差分更新対応), ImageItem, ...   │
├─────────────────────────────────────────────────┤
│ Infrastructure                                  │
│  DB Repository, Settings Repository,            │
│  FileSystemWatcher 抽象化, Metadata Parser      │
└─────────────────────────────────────────────────┘
        横断: ILogger / IDispatcherService / EventAggregator(UIEvents.cs に集約)
```

### 原則

1. **View は表示専用**: コードビハインドには UI 操作の最小限のみ。ロジックは ViewModel か Service へ
2. **static 禁止（新規）**: 既存 static は Phase ごとに interface 化して注入。シングルトンは DI コンテナで管理
3. **状態変更はサービス経由**: タブ・選択・フォルダ遷移は `IFolderNavigationService` に一本化し、MCP からも同一経路で操作できるようにする（現在の McpOpenFolderEvent → HandleFolderSelected の迂回路を廃止）
4. **コレクション更新は差分適用**: 監視ハンドラでのコレクション全入替（D1 の原因）を禁止し、挿入/削除/移動を明示的に行う
5. **非同期境界の明示**: `async void` はイベントハンドラのみ許可。それ以外は `async Task` + 例外観測可能なこと
6. **多言語・DB 設計など既存規約**（AGENTS.md）は従来通り遵守

## 6. フェーズ計画

各フェーズは独立した PR 群として v1.5.x で順次リリースする（凍結しない）。

### Phase 0: 地盤（目安 1–2 週間）

| 作業 | 内容 |
|---|---|
| DI 整備 | Prism のコンテナ登録を一元化。`SettingsHelper`, `DatabaseAccess`, `LogHelper` の interface 抽出（既存 static はラッパーとして残し呼び出しを漸次置換） |
| テスト基盤 | メタデータ解析系（`ComfyUIMetadataParser` / `StableDiffusionParser` / `WebUIMetadataParser`）に実ファイルベースの golden master テストを追加。既知失敗 2 件の修正または削除 |
| カバレッジ測定 | coverlet 導入と CI へのカバレッジレポート追加。Phase ごとの目標達成を数値で追跡できるようにする |
| ロギング | `NavigationDiagnosticsLog` を `ILogger` 抽象へ統合（開発者モードゲートは現状維持） |

**完了基準**: 新規コードが DI 注入でテスト可能であること。パーサー系テストが緑であること。

### Phase 1: ゴッドクラス解体（目安 3–4 週間）

| 作業 | 内容 |
|---|---|
| サムネイルパイプライン | `ThumbnailLoaderHelper`(static) → `IThumbnailPipelineService`。キャッシュ(`ImageCacheDesign.md`)と要求キューを service 内部に隠蔽 |
| ThumbnailListControl 解体 | 3,525 行 → 監視ハンドラ / ソート・フィルタ / D&D / クリップボード / 自動選択に分割し ViewModel+Service へ移動。目標 500 行未満 |
| 差分更新の導入 | 監視イベント時のコレクション全入替を廃止し Add/Remove/Move に（D1 の根本対策）。#48 の診断ログはこの検証に再利用 |

**完了基準**: 当該ファイル 500 行未満。パイプラインのユニットテスト追加。「開いているフォルダへのファイル追加が即反映される」ことを空/非空/一括投入で確認（2026-08-22 の検証手順を流用）。

### Phase 2: シェルとツリー（目安 2–3 週間）

| 作業 | 内容 |
|---|---|
| IFolderNavigationService 新設 | タブ作成/切替/フォルダ遷移/選択復元を集約。`MainWindowViewModel.HandleFolderSelected` と MCP の `open_folder` をここに統合 |
| ツリーの整理 | `FileSystemTreeView_Loaded` の二重初期化解消（D3）、ライフサイクルの明示管理。`ExpandPath` の null フォールバック見直し |
| イベント整理 | View 直使用の EventAggregator を ViewModel/Service 経由に寄せ、イベント定義は `Events/UIEvents.cs` に一元 |

**完了基準**: View からの DB/設定直接参照ゼロ（D6 解消）。#48 再発防止の回帰テスト（選択抑制ロジックの単体テスト）追加。

### Phase 3: v2 判定ゲート

- 設定/DB アクセスの Repository 化完了、Helpers の再編（重複排除・静的排除）を仕上げ、**v2.0.0 としてタグ**
- MCPHost を Application Services 経由に載せ替え、外部操作ツール群（フォルダオープン・レーティング・メタデータ取得等）を services の interface から機械的に公開できる状態にする
- MCP ツールを外部契約として整備する: ツール定義のスキーマ化とバージョニング、2 クライアント（開発エージェント／ローカル LLM）向けの契約テスト、localhost バインドのアクセス方針明記（`MCP_Design.md` 改訂）
- **テスト完了基準**: Application Services 層・ViewModels のカバレッジ 70%（§1 ゴール 3）。全フェーズで追加した回帰テストが CI で緑であること。加えて MCP ツール契約テストが CI に組み込まれ、開発エージェントによる E2E 動作確認（フォルダオープン→状態照会→操作→結果確認）が自動実行できること

## 7. 撤退・ pivot 判断

Phase 1 完了時に以下を評価する:

- 「機能追加の辛さ」が主観でも測定可能な形でも減っていない
- 差分更新導入後も #48 系の不具合が再発している
- 解体により新規回帰が連続している

→ この場合フルスクラッチへ pivot。その際は Phase 0–1 産物（テスト群・interface 定義・更新済み Spec）を仕様の真実源として引き継ぐ。

## 8. リスクと対処

| リスク | 対処 |
|---|---|
| リファクタ中の回帰 | 各抽出前に特性テスト(golden master)。日常運用（EasyReforge 併用）で並行検証。診断ログ基盤を継続利用 |
| 中間状態の長期化 | 各フェーズを独立リリース可能な単位に分割し v1.5.x として出荷し続ける |
| WPF 特有の罠の再学習コスト | 本ドキュメント §3 カタログと `ZoomDesign.md` 等の既存設計書を各フェーズ冒頭で必読にする |
| LLM エージェント作業時の暴走 | AGENTS.md 規約遵守＋フェーズ単位の小さな PR＋テスト必須 |

## 9. 関連ドキュメント

- `docs/Spec.md` / `docs/Design.md`: 仕様の真実源（Phase 進行に合わせ更新）
- `docs/MCP_Design.md`: MCP 対応の基本設計（Phase 3 で Application Services 経由に改訂）
- `docs/ImageCacheDesign.md` / `docs/ZoomDesign.md`: パイプライン解体時の参照設計
- `docs/Implementation.md`: 機能↔実装マップ（解体後に必ず更新）
