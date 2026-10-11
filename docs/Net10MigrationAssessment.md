# Illustra .NET 10 移行検証

## 正式反映の状況

ユーザーの移行指示により、元リポジトリ `C:/Work/Illustra` へ次を反映した。

- アプリ・テストのTFMを `net10.0-windows` に変更。
- build/release CIのSDK指定を `10.0.x` に変更。
- Base.xamlのBackdropブラシ2箇所の自己参照をColorsキーへ修正。
- 配布説明と開発環境ドキュメントを.NET 10へ更新。
- 依存パッケージの一括更新は行っていない。

反映後の元リポジトリで、Debugビルド成功 (6.18秒)、通常群158件成功 (17.33秒)、独立Property/Startup各1件成功 (1.42秒/1.41秒)、win-x64自己完結単一exe publish成功 (6.65秒)。実クリップボード操作テストは除外。`git diff --check` 成功。

証拠: `E:/Worktrees/Illustra/net10-migration-test/migration-results/integrated/` のsummary.json・TRX・ログ。
配布成果物: `E:/Worktrees/Illustra/net10-migration-test/migration-publish/integrated/Illustra.exe`。

先に起動した.NET 10検証worktreeのDebug版と、インストール済み通常版は停止・置換せず保持している。元リポジトリから第3のアプリは起動していない。通常版のインストール先はまだ旧版のまま。GitHub CI実行、commit/push、配布リリース、installer更新は未実施。

既存依存の脆弱性・preview/9系参照の整理、WPFテストの一括実行時干渉、全機能の受入確認は残課題。

以下は正式反映前の比較検証記録であり、「原本未変更」「未反映」等の記述は検証時点の状態を表す。

## 判定

TFM変更だけでビルド・既存の比較対象テスト・自己完結単一exe publishは成立した。今回の比較範囲では.NET 10固有のテスト失敗は確認していない。ただし本番移行・全経路の受入完了ではない。既存テーマの誤定義、脆弱性のある依存、CI設定、未検証の実アプリ経路を処理してから移行する。

## 対象と変更

- 原本: C:/Work/Illustra、検証開始時 HEAD 9f912e0。原本の追跡ファイルは変更せず、未追跡 .omm/ を保持。
- .NET 10検証: E:/Worktrees/Illustra/net10-migration-test (detached worktree)
- .NET 9ネイティブ比較: E:/Worktrees/Illustra/net9-native-baseline (detached worktree)
- .NET 10検証worktreeの変更: src/Illustra.csproj と tests/Illustra.Tests.csproj のTFMをnet10.0-windowsへ変更。追加検証後、src/Themes/Base.xaml の2箇所のColor参照を修正。
- 依存パッケージ更新、元アプリへの反映、commit、push、実アプリの起動は実施していない。

## 実行結果

| 検証 | .NET 9 | .NET 10 |
|---|---|---|
| Debugソリューションビルド | 成功 10.86秒 | 成功 13.51秒 |
| 最初の通常テスト群 | 158/159成功 | 158/159成功 |
| 干渉するテストを除いた通常群 | 158/158成功 15.27秒 | 158/158成功 15.36秒 |
| PropertyPanelInitialSelectionTestsを独立実行 | 1/1成功 | 1/1成功 |
| ThumbnailStartupSelectionTestsを独立実行 | 1/1成功 | 1/1成功 |
| アプリ win-x64自己完結・単一exe publish | 未実施 | 成功 6.63秒 |
| 合成画像・in-memory SQLite probe | 5/5成功 0.88秒 | 5/5成功 1.53秒 |
| probe自己完結単一exe実行 | 未実施 | 5/5成功 6.69秒 |
| probeのexeだけを別フォルダーにコピーして実行 | 未実施 | 5/5成功 1.02秒 |

分離実行した異なる160件は両世代とも成功。実クリップボード操作テストは実行していないため、全テスト成功とは表現しない。
最初の両世代共通失敗: SamePathInitialSelection_RestoresCollapsedPanelVisibility、Application.ShutdownModeへの他スレッドアクセスによるInvalidOperationException。独立プロセスでは両方成功し、既存のWPF Application/Dispatcher共有のテスト干渉と判定。

probeはMagick.NET/SkiaSharpの合成2x2 PNGデコード、ImageSharpで生成したWebPのLibWebP.WebPGetFeatures、System.Data.SQLiteとMicrosoft.Data.Sqliteのin-memory create/insert/selectを実行。アプリ本体のDB管理、動画、アニメーションWebP、実サムネイル経路の証明ではない。
アプリpublish結果はIllustra.exeのみ (236985332 bytes)。アプリexe自体は起動していない。probe単一exeの成功をアプリの受入成功とは扱わない。

## 見つかった課題

### 1. 既存テーマの誤定義 (優先度: 高)

src/Themes/Base.xaml:107-108のSolidColorBrush.ColorがMahApps.Brushes.Window.Background.Backdrop.Acrylic/Auto、つまりBrush自身を参照していた。本来参照するのはMahApps.Colors.Window.Background.Backdrop.Acrylic/Auto。

外部WPF検証ホストから実テーマを読み込み、該当ブラシのColorを解決すると.NET 9と10の両方でStack overflow、exit=3221225725を再現。したがって.NET 10で新規に発生したバグとは断定しない。
.NET 10検証worktreeのみ2箇所をColorsキーへ修正し、Dark/Light双方、Acrylic/Auto双方のリソース解決に成功 (theme-fixed-run.log、exit=0、0.84秒)。外部ホスト用にDark/LightのBase.xamlへのroot-relative pack URIだけをIllustra;component形式に置換した。実アプリ起動やテーマ切替UIは未検証。
元アプリ側には未反映。Microsoftは.NET 10の誤った型のDynamicResourceに対する挙動変更を明記している。
https://learn.microsoft.com/en-us/dotnet/core/compatibility/wpf/10.0/dynamicresource-crash

### 2. SDK・CI・配布説明 (移行時に必要)

- アプリとテストのTFMを揃えてnet10.0-windowsへ変更。
- .github/workflows/build.yml と release.yml の9.0.x SDK指定を10.0.xへ変更。
- 自己完結配布に合わせリリース説明の.NET 9表記を更新。
- CIそのもの、GitHub上のrelease、installerビルド・インストールは未実行。

### 3. 依存のセキュリティ課題 (既存、移行だけでは解消しない)

NuGet public sourceで --vulnerable --include-transitive --format json --no-restore を実行し、vulnerabilities-public.jsonに保存。
- Magick.NET-Q16-AnyCPU 14.5.0: High 26、Moderate 98、Low 38のアドバイザリ項目。
- 推移的依存 SQLitePCLRaw.lib.e_sqlite3 2.1.7: High 1 (GHSA-2m69-gcr7-jv3q)。
これらは監査のアドバイザリ件数であり、独立した悪用可能経路の件数とは限らない。
csprojのNU1903抑制で高深刻度警告がビルド出力に見えない。今回の依存は世代比較のため更新していない。修正版依存への更新・再検証を別に実施する。
通常の監査はカスタムNuGet sourceでexit=1。public source指定の再試行はexit=0。ただしカスタムImageSharpの改変内容・独自パッケージの完全監査ではない。

### 4. 依存世代と新警告

- Microsoft.Data.Sqliteは9.0.0-preview.1.24081.2。安定版への整理を推奨。
- テストのMicrosoft.AspNetCore.TestHost/Mvc.Testingは9.0.2、Mvc.NewtonsoftJsonは9.0.3。TFMだけでは参照の世代は変わらない。使用状況を確認して10系への整合または不要参照削除を行う。
- .NET 10で新たにNU1510: System.Drawing.Common参照が不要の可能性。直ちに削除せず、Shell/描画の実使用を確認する。
- NU1901/NU1902は両世代に存在。ビルドログ内に同じ警告が多数重複するため、ログ行数を脆弱性数と見なさない。
- その他の古いライブラリのバージョンだけで.NET 10非対応とは断定しない。

## 未検証・次の受入条件

1. 実アプリ起動、Dark/Light切替、一覧・選択・設定・タブ・全画面・ビューア再配置。
2. 代表的な画像/アニメーションWebP/動画/音声、Shellサムネイル。
3. アプリDatabaseManagerの合成DBで作成・既存スキーマ読込・評価変更・再起動後の永続化。
4. 実Kestrel/MCPで開始→HTTPツール要求→停止→再開始、ポート競合・終了確認。既存のイベント橋渡しテストだけでは証明しない。
5. CI、実アプリ配布exe、installer、クリーンなWindows環境での受入。
6. 依存更新後に今回と同じ比較テスト・publish・画像/DBprobeを再実行。

## 証拠

`E:/Worktrees/Illustra/net10-migration-test/migration-results/` のnet9/net10-summary.json、additional-summary.json、各TRX・build/tests/publishログ、probe-summary.json、probe-run.log、probe-singlefile-run.log、probe-exe-only.log、theme-original-*、theme-fixed-build.log、theme-fixed-run.log、vulnerabilities-public.json。
合成probeのソース: ../migration-probes/NativeProbe/。コマンド・工程の秒数はsummary JSONに保存。