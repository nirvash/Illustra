# 既知の罠・注意点

## ビルド/環境

- **NuGet 復元に `NUGET_AUTH_TOKEN` が必要**: `src/nuget.config` が GitHub Packages 非公開フィード（`nuget.pkg.github.com/nirvash`）を参照。`SixLabors.ImageSharp 3.1.7-custom1` はここからしか取得できない。
- **PowerShell 5.1 + CP932**: `dotnet` の日本語出力が文字化けする。出力を見るコマンドには毎回 UTF-8 設定プレフィックスを付ける（build-and-test.md 参照）。
- **`*_wpftmp.csproj`**（src/ 配下の複数バリアント）: XAML コンパイラ生成の一時ファイル。編集・削除しない。ビルド警告がこれらのファイル名で出ることがあるが実体は Illustra 本体。
- **XAML タグ内コメントはビルドエラー**。XAML の不備はビルド時に検出されるが、動作不具合は実行時まで潜む。
- `dll/libwebp*.dll` はネイティブ dll が出力先へコピーされる設定（csproj）。`App.xaml.cs` 冒頭の `SetDllDirectory` とセットなので勝手に外さない。
- csproj の NoWarn に CS86xx（nullability）や VSTHRD 系が大量指定済み。警告抑制をさらに追加する前に既存指定を確認。

## 触るな・危険

- **`CreateGitVersionTag` MSBuild ターゲット**（src/Illustra.csproj 内）: 実行すると **git タグの作成と push まで行う**。明示的に `/t:CreateGitVersionTag` を付けない限り通常ビルドでは走らない → うっかり invoke しない。
- バージョン番号は GitVersion 管理。手動書き換え禁止。
- `bin/`, `obj/`, `publish/`, `TestResults/` は生成物。コミットしない。

## 実行時

- **文言リソースの定義漏れは起動時クラッシュ**（Strings.xaml / Strings.ja.xaml の片方だけ追加した場合）。文言追加後は必ずビルドして起動確認。
- レーティング等は SQLite に **ファイルパス文字列** で紐づく。リネームすると紐付けが切れる仕様（README にも記載）。パス正規化の扱いを変える変更は要注意。
- MCP サーバーはアプリ内 Kestrel。Bearer トークン認証付き。トークン比較は定数時間比較、生成は暗号学的 RNG を使用（過去にセキュリティ修正歴あり。緩めないこと）。
- `McpHostManager` の Start/Stop は SemaphoreSlim で直列化されている（並行 start/stop での競合修正の歴史あり）。この構造を壊さない。

## テスト

- テストは NUnit + Moq。主対象は Helpers のメタデータ解析系と MCP ホスト（AspNetCore TestHost）。UI/ViewModel はほぼ未カバー → View 変更は手動確認が必要。
- メタデータ解析のテストにはテスト用 PNG/MP4 を生成するヘルパー（`tests/Helpers/TestPngBuilder.cs`, `TestMp4Builder.cs`）がある。フィクスチャファイルを作り込まなくてよい。
