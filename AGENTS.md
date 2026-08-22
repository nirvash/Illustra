# Illustra - AI Developer Guide

このファイルは AI エージェント（opencode）がこのリポジトリで作業する際のガイドラインです。

## プロジェクト概要

**Illustra** は Windows 用の高速画像ビューア。WPF + .NET 9 で実装され、仮想化サムネイル表示、画像レーティング、Stable Diffusion プロンプト表示、タブ表示などの機能を持つ。

- 対象 OS: Windows 10/11
- フレームワーク: `net9.0-windows`（WPF）
- アーキテクチャ: MVVM（Prism、イベントアグリゲーター）
- ライセンス: MIT

## 開発環境

- OS: Windows（シェルは PowerShell 5.1）
- .NET 9 SDK 必須
- IDE: Visual Studio 2022（人間の開発者向け）
- **シェル出力の文字コード注意**: 実シェルは PowerShell 5.1 であり、コンソール既定は **CP932（Shift-JIS）**。`dotnet` 等の CLI は UTF-8 で出力するため、そのままだと日本語出力が文字化けする。出力内容を確認するコマンドでは先頭に次を付けること（呼び出し毎にシェルは新規起動されるため毎回必要）:

  ```powershell
  [Console]::OutputEncoding = [Text.Encoding]::UTF8; $OutputEncoding = [Text.Encoding]::UTF8
  ```


## コマンド

```bash
# ビルド
dotnet build Illustra.sln

# 実行（メインアプリ）
dotnet run --project src/Illustra.csproj

# テスト（NUnit）
dotnet test Illustra.sln

# 特定テストのみ実行
dotnet test tests/Illustra.Tests.csproj --filter "FullyQualifiedName~<TestName>"
```

- バージョン番号は GitVersion で管理（`src/GitVersion.yml`）。手動でバージョンを書き換えないこと
- `*_wpftmp.csproj` は XAML コンパイラが生成する一時ファイル。編集・削除しないこと
- lint/format の専用コマンドは存在しない。ビルド警告をゼロに保つこと

## ソリューション構成

| プロジェクト | パス | 役割 |
|---|---|---|
| Illustra | `src/Illustra.csproj` | メインアプリケーション |
| Illustra.Tests | `tests/Illustra.Tests.csproj` | テスト（NUnit + Moq） |

## ソースコード構造（src/ 配下）

```
Views/      XAML + code-behind（Partial クラスで機能分割）
ViewModels/ ViewModel（Prism ベース）
Models/     データモデル
Services/   サービス層
Controls/   再利用可能なカスタムコントロール
Events/     イベントアグリゲーター用イベント定義（UIEvents.cs、McpEvents.cs）
Helpers/    ユーティリティ
Converters/ XAML 値コンバーター
Themes/     テーマ（MahApps.Metro ベース、ライト/ダーク）
Resources/  多言語リソース（Strings.xaml / Strings.ja.xaml）
Mcp/        MCP サーバー機能（アプリ内 Kestrel ホスト、Streamable HTTP、ツール実装）
```

## 重要なルール

### 多言語対応（必須）

- 文言は `Resources/Strings.xaml`（英語）と `Resources/Strings.ja.xaml`（日本語）の **両方** に定義する
- **片方だけだと起動時にクラッシュする**
- 文言 ID は `String_<カテゴリ>_<名称>` 形式
- 参照は `{DynamicResource String_XXX}` を使う

### コード規約

- 機能ごとにクラス分割または Partial クラス化し、1 ファイルが巨大にならないようにする
- 似たコードは共通化し、再利用可能なコンポーネントとして切り出す
- 画面間通信はイベントアグリゲーターを使い、イベントは `Events/UIEvents.cs` に定義する
- `Math.Min()` のように Math ライブラリのメソッドは大文字始まりで呼ぶ
- 新しい名前空間を使ったら `using` を忘れない
- ビルド警告（warning）を出さない・残したままにしない

### ダイアログデザイン

- ボタン配置は **キャンセルが左、OK が右**（日英共通）

### データ管理

- レーティング等は SQLite（linq2db / sqlite-net）でファイルパスに紐付けて永続化
- 設定のシリアライズは Newtonsoft.Json
- DB 設計ドキュメント: `docs/DatabaseDesign.md`

## 主要ドキュメント

| ドキュメント | 内容 |
|---|---|
| `docs/Implementation.md` | 機能 ↔ 実装箇所マッピング（コード理解の出発点） |
| `docs/Spec.md`, `docs/Design.md` | 仕様・設計 |
| `docs/Rule.md` | 開発ルール詳細 |
| `docs/MCP_v2_Design.md` | MCP サーバー設計（アプリ内 Kestrel、Streamable HTTP、ツール仕様） |
| `docs/ImageCacheDesign.md` | 画像キャッシュ設計 |
| `docs/ZoomDesign.md` | ズーム/パン設計 |

実装前に対応する設計ドキュメントがあるか確認し、大きな変更時にはドキュメントを更新すること。

## Git 運用

- コミットメッセージは日本語で Conventional Commits スタイル: `feat: xxxを追加`、`fix: xxxの不具合を修正`
- 作業ブランチから master への PR で開発
- リリースは GitHub Actions（`.github/workflows/release.yml`）

## 注意事項

- UI 変更後は必ずビルドして動作確認する（XAML エラーは実行時まで気づきにくい）
- テストを追加・更新した場合は `dotnet test` で通ることを確認する
- `bin/`、`obj/`、`publish/`、`TestResults/` は生成物。コミットしない
