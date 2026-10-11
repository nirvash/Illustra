# 依存ライブラリのセキュリティ対応

## 方針

単にパッケージ番号を更新するのではなく、不要な依存を削除し、実際にロードされるネイティブエンジンと機能互換性を確認する。検証作業ツリーは `E:/Worktrees/Illustra/dependency-security`、開始時点の原本コミットは `887b87a`。稼働アプリ・ユーザー画像・実DBは検証に使用しない。

## 調査と確定した事実

- Magick.NETはWebpPlayerViewModelの未使用usingのみで、実処理では使われていなかった。削除する。
- Microsoft.Data.Sqliteの旧preview参照を削除するだけでは不十分。実接続していたSystem.Data.SQLite.Core 1.0.119はSQLite 3.46.1を同梱し、CVE-2025-6965の修正境界3.50.2より古い。
- `linq2db.SQLite`はSystem.Data.SQLite依存を再導入するため、既存linq2db本体を維持して、この補助パッケージも削除する。
- Microsoft.Data.Sqlite 10.0.12とSQLitePCLRaw.bundle_e_sqlite3 2.1.13に一本化した検証で、実エンジンはSQLite 3.53.3、実接続型はMicrosoft.Data.Sqlite.SqliteConnectionだった。
- ImageSharp独自版はUTF-16BE/UTF-8 EXIF UserComment機能のためのfork。公式4.1.3は隔離package probeの監査警告なしだったが、Releaseビルドはライセンスファイル未設定で失敗。ライセンス取得・有料契約・ライセンス検証回避は行っていない。
- ユーザーはImageSharpも削除し、既存SkiaSharp等で機能を維持する方向を選択した。文字化け、透明度、stealthメタデータ、既存メタデータ保持を回帰検証してから反映する。

## SQLiteの実検証

- 修正前の境界テストはRED。実接続System.Data.SQLite、SQLite 3.46.1。
- 修正後の親実行では、provider/native境界、FileNodeModelのCRUD・再読込・日本語・日時・NULL、busy判定の3テストが成功。
- 元のSystem.Data.SQLite/linq2dbで作った合成FileNodeModel DBを、新Microsoft providerで読み出して評価・NULL日時を更新し、さらに旧providerで再読込する往復検証が成功。ユーザーDBは読まない。
- スキーマ・DBパスは維持。接続文字列のVersion=3指定はMicrosoft形式では不要なので削除し、SqliteConnectionStringBuilderを使用。linq2db providerはProviderName.SQLiteMSを明示。
- 子の復元はsandboxのネットワーク制限で失敗したが、親が公開NuGet sourceから復元してGREENを確認した。

## 証拠の場所

- `E:/Worktrees/Illustra/dependency-security/security-results/sqlite-provider.trx`
- 同worktreeの `sqlite-parent-restore.log` / `sqlite-parent-test.log`
- `C:/Users/nirva/AppData/Local/hermes/profiles/agent-harness/cache/scratch/sqlite-compat/` の合成互換probeソースとold-create/new-update/old-readbackログ
- ライセンス確認probe: `C:/Users/nirva/AppData/Local/hermes/profiles/agent-harness/cache/scratch/security-package-probe/`

## 統合検証済みの結果（原本反映前）

- ImageSharpをアプリ・テストの依存グラフから削除。SkiaSharpで初回フレームをUnpremul BGRAとして読み、alpha 0/1/254/255・stealth・クリップボード用Bgra32を確認。
- JPEG/PNG/WebPのUTF-16BEコメント、日本語・絵文字、他のEXIF・PNGテキスト・アニメーションデータ保持を合成画像で検証。画像データは再圧縮しない。
- 高深刻度警告NU1903の抑制を解除。
- 不要になったプロジェクト内の非公開NuGetフィード・資格情報用環境変数参照を削除。トークンを外した通常restoreを `--force --no-cache` で成功確認。ユーザーの資格情報・global NuGet設定は変更していない。
- Debug build成功（5.01秒）。通常群189件（16.59秒）、WPF初期選択と起動選択を独立プロセスで各1件（1.41/1.42秒）、合計191件成功。
- 別途、保存後の表示モデルまで読むJPEG/PNG/WebPの3件も成功。BOMの表示不具合はこの親の再現では発生しなかったため、ImagePropertiesModelには不要な修正を加えていない。
- Release win-x64自己完結単一exeのpublish成功（6.05秒）。native probeも単一exeでpublishし、exeだけを新しいディレクトリにコピーしてPNG・WebP/libwebp・SQLite engine/CRUDの3項目成功。アプリ全画面の受入とは区別する。
- 公開NuGet sourceを指定した推移依存込み監査で、アプリ・テストとも既知脆弱性報告0。assets.jsonでもMagick.NET、ImageSharp、System.Data.SQLite.Core、linq2db.SQLiteが残っていないことを確認。

## 残作業・検証限界

初回の独立レビューは保存の比較→置換の競合を指摘し不合格。修正前の決定的回帰テストで更新消失を再現した後、canonical pathをキーにするWindows named Mutexでsnapshot全byte比較→原子的置換の同期commitを排他する修正を実施。Mutexをawaitを跨いで保持せず、timeout/abandoned/cleanupを処理する。同じcanonical pathを使う更新済みIllustraの協調保存が保証対象で、非協調外部writerやsymlink/hardlink別名まで完全CASを保証しない。

親のEXIF保存・競合テスト27件が全成功。旧エンジンで作った合成DBのスキーマ・格納型保持テストも成功。修正後の統合は通常198件（16.11秒）と分離WPF2件の合計200件成功、Release publish 5.76秒、単一exe-only native probe 3項目成功、公開NuGet監査はアプリ・テストとも既知脆弱性報告0。初回の証拠は `security-results-before-concurrency-fix` に保存し、修正後証拠は `security-results` に保存した。

独立再レビューは `deleg_27385d49` で合格。レビュー前後と原本反映前のsource hashが一致し、対象16ファイルの同一性を親でも確認した。原本 `C:/Work/Illustra` へ必要なソース・テスト・設定・資料を反映済み。検証artifact群は原本へコピーせず、`.omm/` を保持した。

原本でトークン不在の通常restore（`--force --no-cache`）成功、Debug build 9.35秒成功、通常198件17.42秒と分離WPF各1件1.39秒の合計200件成功。Release win-x64自己完結単一exe publish 10.74秒成功。公開NuGet推移依存込み監査はアプリ・テストとも既知脆弱性報告0、原本assetsにも除去対象packageなし。`git diff --check` 成功。

原本の証拠は `E:/Worktrees/Illustra/dependency-security/security-results/original/`（summary.json、ログ、TRX）。publish出力は `E:/Worktrees/Illustra/dependency-security/security-publish/original/Illustra.exe`。元の追跡ファイルは `C:/Users/nirva/AppData/Local/hermes/profiles/agent-harness/cache/scratch/illustra-security-original-backup/` に反映前バックアップし、反映manifestを同scratchに保存した。

本件のソース変更はコミット `898942f` にまとめ、通常版をローカルデプロイ済み（下記「コミット・ローカルデプロイ実績」参照）。push・installer更新は未実施。別process/Windows sessionのMutex実動作とcleanupエラー診断の改善は再レビューの非阻害提案として残る。

レビューの追加提案を受け、実際の旧System.Data.SQLiteエンジンで生成した12,288 bytesの合成DBを `tests/Fixtures/legacy-system-sqlite.db` に追加した。fixtureの生成根拠は同名mdに記録。既存DDL・列情報・格納型まで新providerの更新前後で比較する回帰テストも成功。

実クリップボードを変更するfixtureはユーザーの内容を保護するため除外。子sandboxでは実保存成功3件が置換権限のためスキップされたが、親環境のEXIFテスト27件はスキップなしで成功した。ユーザー実DB・画像を使う全経路、installer、GitHub CIは未検証。

不正・未対応構造は元画像を保持して拒否する。TIFFの他の既存値を移動しない方式のため、EXIF更新を繰り返すとメタデータが増え、JPEGの約64KiB上限に達する場合がある。上限時に非原子的な上書きや保存成功扱いにしない。

追加証拠: 同worktreeの `security-results/integrated-summary.json`、分離テストTRX、`exif-display-red.trx`（名前はRED候補だが結果はGREEN）、`public-default-restore.log`、`vulnerabilities-src.log` / `vulnerabilities-tests.log`、`probe-exe-only-run.log`。

## 残る参照の整理（残課題4、対応済み）

- テストコードに使用箇所がない `Microsoft.AspNetCore.TestHost 9.0.2`、`Microsoft.AspNetCore.Mvc.Testing 9.0.2`、`Microsoft.AspNetCore.Mvc.NewtonsoftJson 9.0.3` は更新ではなく削除した。関連する古いコメントも除去。
- `System.Drawing` 自体は `VideoThumbnailHelper` のShell Bitmap→PNG変換で実使用。処理は削除しない。WPF/.NET 10が同アセンブリを供給するため `System.Drawing.Common 9.0.2` の明示PackageReferenceのみ削除した。
- 原本の復元後assets.jsonでアプリ・テストとも上記4パッケージが残っていないことを確認。NU1510警告も解消。
- 原本Debug build 3.05秒、分離200テスト成功（通常198件17.03秒、独立WPF各1件1.39/1.41秒）、Release自己完結単一exe publish 4.31秒。公開NuGet推移依存込み監査はアプリ・テストとも既知脆弱性報告0。
- 合成2×2 System.Drawing.BitmapをPNGに保存してWPF BitmapImageへ変換するprobeが成功。実ロードは `System.Drawing.Common, Version=10.0.0.0`。自己完結単一exeを作り、exeだけを別フォルダーに移した実行も成功。Windows Shellが実動画からBitmapを取得する全経路はこのprobeの対象外。
- 検証ログ/TRX/summary: `E:/Worktrees/Illustra/dependency-security/reference-cleanup/results/`。配布用exe: 同 `reference-cleanup/publish/Illustra.exe`。probeソース・ログ・単一exe: 同 `reference-cleanup/probe/`。
- 先行の未コミット依存修正と `.omm/` を保持した状態で検証した。この段階ではアプリ停止・起動・commit/push・ローカルデプロイを行わず、その後の実績は次節に記録。

## コミット・ローカルデプロイ実績

- ソースコミット: `898942f6e1d1b47a20360e5db1887111d0290b99`（`fix: 不要依存を削除しSQLiteと画像処理を安全化`）、対象28ファイル。`.omm/` は含めず、pushしていない。
- コミット後にRelease/win-x64/自己完結/単一exeを発行（6.02秒成功）。配置元は `E:/Worktrees/Illustra/dependency-security/local-deploy/publish/Illustra.exe`。
- 配置先: `C:/Users/nirva/AppData/Local/Illustra/Illustra.exe`。配置・起動は3.83秒。配置元と配置先のSHA256は `2B3A07B3C65C6AD1C4E66D050484D9602710F98BDF2E167DEC58F71CAEFB8B0E` で一致。
- 旧exeバックアップ: `C:/Users/nirva/AppData/Local/Illustra/backups/Illustra-pre-dependency-security-20261011-220206.exe`。
- 全Illustraプロセスの終了を確認した状態で、既存 `illustra.db` もcold copyしてhash一致確認。バックアップ先は `C:/Users/nirva/AppData/Local/Illustra/backups/data-pre-dependency-security-20261011-220206/`。設定・ユーザー画像は手動変更していない。強制終了していない。
- 新通常版はPID14644、ウィンドウ5767356、応答あり。実画面で一覧・プロパティ・タブの表示を確認。MCPは同PIDのlocalhost:5149で待受し、HTTP initialize→tools/list→get_app_status(targetTab=active) が成功。バージョン1.5.28、タブ4件・読込169ファイル・選択1件を読み戻した。起動後にユーザーの選択や評価を変更していない。
- デプロイ前の既存Hermes MCP接続は利用できず、IllustraのツールもHermes側の一覧から外れていたため、新通常版の実サーバーへ標準MCP HTTPを直接呼び出して検証した。Hermes設定・認証・アクセス制限は変更していない。Hermes側の再接続そのものは未確認。
- 実績JSONとMCP検証: `E:/Worktrees/Illustra/dependency-security/local-deploy/deployment.json` / `mcp-status-verification.json`。発行ログは同 `publish.log`。
- 実画像のEXIF更新・評価変更による保存を伴う全経路、外部アプリとの同時編集、installer、GitHub CIは引き続き未検証。既存データを変更しない起動・表示・MCP状態読取りを今回のデプロイ受入とした。

## 根拠

- https://system.data.sqlite.org/home/doc/trunk/www/news.wiki
- https://github.com/advisories/GHSA-2m69-gcr7-jv3q
- https://github.com/advisories/GHSA-wmxv-xphr-5c9g
- https://github.com/advisories/GHSA-j3p4-wp97-rph4
- https://github.com/advisories/GHSA-j9gm-c75j-xc9q
- https://github.com/advisories/GHSA-jjfr-hcj7-qf5w
