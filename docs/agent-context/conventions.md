# プロジェクト固有の規約

一般的な C# / WPF の作法は書かない。このリポジトリ特有のものだけ。

## アーキテクチャ規約

- 画面間通信は **必ずイベントアグリゲーター**。イベント定義は `src/Events/UIEvents.cs`（MCP 関連は `McpEvents.cs`）に追加。View 間の直接参照や Singleton への直接アクセスで代用しない。
- 機能ごとにクラス分割 or Partial クラス化。巨大ファイルを作らない。既存例: `MainWindow` = 本体 + `MainWindow.KeyboardHandler.cs` + `MainWindow.Properties.cs`。
- 似たコードは共通化して Helper / Service に切り出す。
- DI: Prism + DryIoc。新サービスは `App.xaml.cs` の `RegisterTypes` に登録しコンストラクタ注入で受ける。

## 多言語対応（厳守）

- 文言は `src/Resources/Strings.xaml`（英）と `Strings.ja.xaml`（日）の **両方** に定義。片方だけだと起動時クラッシュ。
- 文言 ID: `String_<カテゴリ>_<名称>` 形式。XAML からは `{DynamicResource String_XXX}` で参照。

## UI 規約

- ダイアログのボタンは **キャンセル左・OK 右**（日英共通）。
- XAML タグ内にコメントを書かない（ビルドエラー）。
- 新規 View は `Views/`、新規カスタムコントロールは `Controls/`、値コンバーターは `Converters/` に置く。

## データ・永続化規約

- 設定値: `Models/AppSettingsModel` にプロパティ追加（デフォルト値付き）→ `SettingsHelper.GetSettings()` で読む。JSON 保存（Newtonsoft.Json）。
- レーティング等のパス紐付けデータ: SQLite。低レベル `DatabaseAccess` / 高レベル `DatabaseManager` の責務分界を維持。スキーマ設計は `docs/DatabaseDesign.md`。
- DB スキーマや主要機能を変えたら対応する docs/ の設計ドキュメントも更新。

## コード品質

- 新しい警告を増やさない。既存警告は多数あるため「ゼロ」は不可能。NU1901/NU1902（依存パッケージ脆弱性）は既知・対応不要。
- Null 許容警告（CS86xx 系）は csproj の NoWarn で抑制されている。null チェックは実害がある箇所だけ意識する。

## Git 規約

- 作業言語・ドキュメント・コミットメッセージは日本語。
- コミットメッセージ: Conventional Commits（`feat: xxxを追加` / `fix: xxxを修正`）。**プレフィックスはリリースノート自動生成に使われる**ので正しく付ける。
- ブランチ: `feat/*`・`feature/*` → `master` へ PR。
