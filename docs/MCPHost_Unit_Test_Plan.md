# MCPHost ユニットテスト計画 (NUnit 版)

## 1. テストの目的

*   `McpController` が HTTP リクエストを正しく処理し、`APIService` の適切なメソッドを呼び出すことを保証する。
*   `APIService` が `McpController` からの要求に応じて、`IEventAggregator` を介して正しいイベントを発行し、WPF 側からの結果 (またはエラー) を非同期に処理できることを保証する。
*   将来的なリファクタリングや機能追加に対する安全網を構築する。

## 2. テスト対象

*   **`Illustra.MCPHost.Controllers.McpController`:**
    *   HTTP リクエスト (ルート、メソッド、パラメータ) に基づいて、注入された `APIService` の適切なメソッド (`ExecuteToolAsync`, `GetInfoAsync` など) を正しい引数で呼び出すこと。
    *   `APIService` からの戻り値 (結果または例外) に基づいて、適切な `IActionResult` (例: `OkObjectResult`, `StatusCodeResult(500)`) を返すこと。
*   **`Illustra.MCPHost.APIService`:**
    *   コントローラーから渡された引数に基づいて、適切なイベント引数 (`ExecuteToolEventArgs` など) を作成すること。
    *   `IEventAggregator` を介して正しいイベント (`ExecuteToolEvent` など) を発行 (`Publish`) すること。
    *   イベント引数に含めた `TaskCompletionSource` を介して、WPF 側からの結果 (`SetResult`) または例外 (`SetException`) を非同期に待機し、適切に処理すること。
    *   (もし実装されていれば) キャンセル処理 (`CancellationToken`) が正しく機能すること。

## 3. テスト戦略

*   **テストプロジェクト:** 既存の `tests/Illustra.Tests.csproj` を使用します。
*   **テストフレームワーク:** **NUnit** を使用します。
*   **モックライブラリ:** Moq を使用して依存関係をモック化します。
    *   `McpController` のテストでは `APIService` をモックします (`Mock<APIService>`)。
    *   `APIService` のテストでは `IEventAggregator` と `PubSubEvent<T>` をモックします (`Mock<IEventAggregator>`, `Mock<PubSubEvent<T>>`)。
*   **テスト構成:**

    ```mermaid
    graph TD
        subgraph Test Project (Illustra.Tests)
            McpControllerTests[McpControllerTests (NUnit)] -- Uses --> MockApiService[Mock<APIService>]
            McpControllerTests -- Tests --> McpController[McpController]

            APIServiceTests[APIServiceTests (NUnit)] -- Uses --> MockEventAggregator[Mock<IEventAggregator>]
            APIServiceTests -- Uses --> MockPubSubEvent[Mock<PubSubEvent>]
            APIServiceTests -- Tests --> APIService[APIService]
        end

        subgraph Target Project (Illustra.MCPHost)
            McpController -- Depends on --> APIService
            APIService -- Depends on --> IEventAggregator[IEventAggregator]
            APIService -- Uses --> PubSubEvent[PubSubEvent<T>]
        end

        subgraph Shared Dependencies
            IEventAggregator
            PubSubEvent
            ToolExecuteRequest[...]
            IActionResult[...]
        end

        MockApiService -- Implements --> APIService
        MockEventAggregator -- Implements --> IEventAggregator
        MockPubSubEvent -- Implements --> PubSubEvent
    ```

## 4. 主要なテストケース

*   **`McpControllerTests`:**
    *   `ExecuteTool_ValidRequest_CallsApiServiceAndReturnsOk`: 正常なリクエストで `APIService.ExecuteToolAsync` が呼ばれ、`OkObjectResult` が返る。
    *   `ExecuteTool_ApiServiceThrowsException_ReturnsInternalServerError`: `APIService.ExecuteToolAsync` が例外を投げた場合に `StatusCode(500)` が返る。
    *   `GetInfo_ValidRequest_CallsApiServiceAndReturnsOk`: 正常なリクエストで `APIService.GetInfoAsync` が呼ばれ、`OkObjectResult` が返る。
    *   `GetInfo_ApiServiceThrowsException_ReturnsInternalServerError`: `APIService.GetInfoAsync` が例外を投げた場合に `StatusCode(500)` が返る。
    *   (必要に応じて) 不正なリクエスト (モデルバインディングエラーなど) に対するテスト。
*   **`APIServiceTests`:**
    *   `ExecuteToolAsync_ValidCall_PublishesCorrectEvent`: `ExecuteToolAsync` 呼び出し時に、正しい引数で `ExecuteToolEvent` が発行される。
    *   `ExecuteToolAsync_SubscriberSetsResult_ReturnsResult`: イベント購読側が `TaskCompletionSource.SetResult` を呼び出した場合に、メソッドがその結果を返す。
    *   `ExecuteToolAsync_SubscriberSetsException_ThrowsException`: イベント購読側が `TaskCompletionSource.SetException` を呼び出した場合に、メソッドが例外をスローする。
    *   `GetInfoAsync_ValidCall_PublishesCorrectEvent`: `GetInfoAsync` 呼び出し時に、正しい引数で対応するイベントが発行される。
    *   `GetInfoAsync_SubscriberSetsResult_ReturnsResult`: イベント購読側が `TaskCompletionSource.SetResult` を呼び出した場合に、メソッドがその結果を返す。
    *   `GetInfoAsync_SubscriberSetsException_ThrowsException`: イベント購読側が `TaskCompletionSource.SetException` を呼び出した場合に、メソッドが例外をスローする。
    *   (必要に応じて) キャンセル処理に関するテスト。

## 5. 実装手順

1.  `tests/Illustra.Tests.csproj` に NUnit と Moq の NuGet パッケージが含まれているか確認し、なければ追加する (`NUnit`, `NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk`, `Moq`)。
2.  `tests` ディレクトリ内に `MCPHost` 用のフォルダ (例: `tests/MCPHost`) を作成する。
3.  `McpControllerTests.cs` を作成し、NUnit の属性 (`[TestFixture]`, `[Test]`, `[SetUp]`) と Moq を使ってテストケースを実装する。
4.  `APIServiceTests.cs` を作成し、NUnit の属性と Moq を使ってテストケースを実装する。
5.  テストを実行し、すべてパスすることを確認する。
