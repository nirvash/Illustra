# Illustra - AI エージェントガイド

## Project Overview

Windows 用画像ビューア（WPF + .NET 9、`net9.0-windows`）。MVVM（Prism 9 + DryIoc、イベントアグリゲーター）。

- ソリューション: `src/Illustra.csproj`（メインアプリ）+ `tests/Illustra.Tests.csproj`（NUnit + Moq）
- ロジックの大半は `src/Helpers/`。UI は `Views/`（Partial 分割）+ `ViewModels/`。画面間通信はイベントアグリゲーター（UI 共通 = `src/Events/UIEvents.cs`、MCP 関連 = `src/Events/McpEvents.cs`）
- 永続化: 設定 = JSON（`SettingsHelper` → `AppSettingsModel`）、レーティング等 = SQLite（`DatabaseManager`）
- `src/Mcp/`: アプリ内 Kestrel で動く MCP サーバー（`http://localhost:5149/mcp`）
- 作業言語・ドキュメント・コミットメッセージは日本語。コミットは Conventional Commits（`feat:`/`fix:` プレフィックスがリリースノート生成に使われる）

環境の罠（毎回影響）: NuGet 復元には `NUGET_AUTH_TOKEN` 環境変数が必要。PowerShell 出力を見るコマンドには UTF-8 設定プレフィックスが必要。詳細は `docs/agent-context/build-and-test.md`。

## Context Policy

Minimize context usage.

- Do NOT scan or preload the entire repository at the start of a task.
- Do NOT automatically load all documents referenced by this file.
- Load files only when they are relevant to the current task.
- Prefer search and symbol lookup before opening source files.
- Read the smallest useful set of files.
- Do not reread files already sufficiently represented in the current context unless verification is necessary.
- Use project summaries before exploring implementation details.
- Source code is authoritative if documentation is stale or conflicts with implementation.

## Context Index

Read these only when relevant:

- アーキテクチャと構成要素の関係、データフロー、設計上の制約:
  `docs/agent-context/architecture.md`
- モジュール別の責務と「この問題ならどのファイルを見るべきか」の地図:
  `docs/agent-context/modules.md`
- ビルド・テスト・実行コマンド、検証の順序（狭い→広い）:
  `docs/agent-context/build-and-test.md`
- プロジェクト固有のコーディング規約（多言語対応・UI・データ・Git）:
  `docs/agent-context/conventions.md`
- 既知の罠、生成ファイル、危険な操作、プラットフォーム固有の挙動:
  `docs/agent-context/known-gotchas.md`

セッション開始時にこれらを一括で読み込まないこと。

## Task Workflow

For each task:

1. Identify the affected subsystem from AGENTS.md.
2. Load only the relevant agent-context document if necessary.
3. Search for the specific symbols, classes, methods, or files involved.
4. Read only the files needed to understand or modify the behavior.
5. Make the change.
6. Run the narrowest relevant verification first.
7. Expand the investigation only if the narrow approach is insufficient.

Do not explore unrelated modules merely to gain general familiarity with the repository.

## PR Review Response Workflow

When addressing PR review comments (e.g. CodeRabbit):

1. Convert all review items into a todo list; the parent agent tracks each item's state (`unstarted → in progress → implemented → verified`).
2. For each item: delegate investigation to a subagent when bounded, then present the user with a problem summary and a fix proposal, and wait for approval before implementing.
3. After approval: implement → verify with `dotnet build Illustra.sln` (0 errors) → commit as an individual commit with a Japanese Conventional Commits message.
4. Group multiple review items into one delegation only when they touch the same file or the same area (e.g. several fixes in one ViewModel).
5. Do not mark an item `verified` based solely on the subagent's completion report; the parent confirms it first.
6. Push only when the user asks.

## Search-First Policy

Before broadly reading source code:

1. Search for the relevant symbol or feature name.
2. Identify likely entry points.
3. Follow only the dependencies relevant to the task.
4. Open complete source files only when their implementation is actually needed.

Avoid broad directory scans when targeted search can identify the relevant code.

## Exploration Limits

Stop exploring once enough information has been obtained to perform the requested change safely.

Do not:
- inspect every implementation of an interface unless required
- read every file in a directory
- recursively inspect unrelated callers and callees
- inspect generated files unless diagnosing generation or build behavior
- inspect unrelated tests
- inspect historical, backup, or archived files without a specific reason

Before opening another large file, consider:

"Will this file materially change the implementation, diagnosis, or verification plan?"

If not, do not read it.

## Parent Agent Policy

The primary agent is responsible for:

- maintaining the authoritative task or review checklist
- deciding which item is currently active
- delegating bounded implementation or investigation tasks
- reviewing subagent results
- verifying completion
- keeping the parent context concise

The parent agent should avoid performing broad implementation exploration when it can delegate a well-bounded task.

When a subagent returns, retain only the information needed to continue:

- conclusion
- files changed
- important design implications
- verification performed
- unresolved risks

Do not copy large source excerpts or full investigation logs into the parent context.

レビュー指摘を順に処理する場合は各項目の状態を親側で管理する:
`unstarted → in progress → implemented → verified`
subagent の修正完了だけでは `verified` にしない。親が確認した後でのみ更新する。

## Subagent Policy

委譲用 subagent 定義: `.opencode/agent/bounded-task.md`。subagent に委譲する際はこの定義を使う（または同等の方針をプロンプトに含める）。1 つの小さな修正ごとに複数の subagent を起動せず、まとまった 1 単位で委譲する。

## Maintaining Agent Context

Update agent-context documentation only when a change materially affects:

- architecture
- module responsibilities
- major entry points
- important dependencies
- build/test procedures
- repository-wide conventions
- important known pitfalls

Do not update summaries for routine local implementation changes.

Keep all updates concise.
