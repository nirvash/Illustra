# ビルド・テスト・実行

## 前提環境

- .NET 9 SDK、Windows 専用。
- **NuGet 復元には `NUGET_AUTH_TOKEN` 環境変数が必要**（packages:read 権限の GitHub トークン）。`src/nuget.config` の非公開フィードから `SixLabors.ImageSharp 3.1.7-custom1` を取得するため。未設定だと復元失敗。
- 実シェルは PowerShell 5.1（既定 CP932）。**出力を見るコマンドには毎回先頭に付ける**:

  ```powershell
  [Console]::OutputEncoding = [Text.Encoding]::UTF8; $OutputEncoding = [Text.Encoding]::UTF8
  ```

## コマンド

```bash
# 復元（トークン設定済みであること）
dotnet restore Illustra.sln

# ビルド
dotnet build Illustra.sln

# 実行（WPF アプリが起動する）
dotnet run --project src/Illustra.csproj

# テスト全部（41 テスト程度・数十秒）
dotnet test Illustra.sln

# 特定テストのみ
dotnet test tests/Illustra.Tests.csproj --filter "FullyQualifiedName~<テスト名>"
```

- lint/format コマンドは存在しない。規約は `conventions.md` 参照。

## 検証の順序（狭い → 広い）

```text
1. 影響する単一テスト: dotnet test tests/Illustra.Tests.csproj --filter ...
   （パーサ/DB/MCP 系の変更時のみ。View/XAML 変更は該当テストなし）
2. ビルド: dotnet build Illustra.sln  ← 全ファイル修正後に必須（エラーゼロ確認）
3. 関連が広い場合のみフルテスト: dotnet test Illustra.sln
```

- **毎回の full test は不要**。ローカル修正の基本検証はビルド。テストは対象ドメイン変更時のみ。
- XAML エラーはビルドで検出されるが挙動不具合は実行時まで分からない。UI 変更はアプリ起動して目視確認が確実。

## リリース（通常タスクでは触らない）

- タグ `v*.*.*` を push → GitHub Actions（`.github/workflows/release.yml`）が Release ビルド + 単一 exe publish + Inno Setup インストーラ + ZIP 作成まで自動実行。
- バージョン番号は GitVersion 管理。手動書き換え禁止（詳細は known-gotchas.md）。
