# MCPHost テストケース一覧

## McpControllerTests

### `ExecuteTool_ValidRequest_CallsApiServiceAndReturnsOk`
- **目的**: 正常なリクエストで `APIService.ExecuteToolAsync` が呼ばれ、`OkObjectResult` が返ることを確認
- **入力**:
  - ツール名: "test-tool"
  - パラメータ: { param1: "value1" }
- **期待結果**:
  - `APIService.ExecuteToolAsync` が1回呼ばれる
  - `OkObjectResult` が返る
  - 結果の値が非null

### `ExecuteTool_ApiServiceThrowsException_ReturnsInternalServerError`
- **目的**: `APIService.ExecuteToolAsync` が例外を投げた場合に `StatusCode(500)` が返ることを確認
- **入力**:
  - ツール名: "test-tool"
  - パラメータ: { param1: "value1" }
  - モック設定: `ExecuteToolAsync` が例外をスロー
- **期待結果**:
  - `ObjectResult` が返る
  - ステータスコードが500

### `GetInfo_ValidRequest_CallsApiServiceAndReturnsOk`
- **目的**: 正常なリクエストで `APIService.GetInfoAsync` が呼ばれ、`OkObjectResult` が返ることを確認
- **入力**:
  - ツール名: "test-tool"
  - ファイルパス: "test/path"
- **期待結果**:
  - `APIService.GetInfoAsync` が1回呼ばれる
  - `OkObjectResult` が返る
  - 結果の値が非null

### `GetInfo_ApiServiceThrowsException_ReturnsInternalServerError`
- **目的**: `APIService.GetInfoAsync` が例外を投げた場合に `StatusCode(500)` が返ることを確認
- **入力**:
  - ツール名: "test-tool"
  - ファイルパス: "test/path"
  - モック設定: `GetInfoAsync` が例外をスロー
- **期待結果**:
  - `ObjectResult` が返る
  - ステータスコードが500

## APIServiceTests

### `ExecuteToolAsync_ValidCall_PublishesCorrectEvent`
- **目的**: `ExecuteToolAsync` が正しい引数でイベントを発行することを確認
- **入力**:
  - ツール名: "test-tool"
  - パラメータ: { param1: "value1" }
- **期待結果**:
  - イベントが発行される
  - イベント引数に正しい値が設定されている

### `ExecuteToolAsync_SubscriberSetsResult_ReturnsResult`
- **目的**: イベント購読側が結果を設定した場合、その結果が返ることを確認
- **入力**:
  - 期待結果: "test-result"
- **期待結果**:
  - 設定した結果が返る

### `ExecuteToolAsync_SubscriberSetsException_ThrowsException`
- **目的**: イベント購読側が例外を設定した場合、その例外がスローされることを確認
- **入力**:
  - 期待例外: Exception("Test error")
- **期待結果**:
  - 設定した例外がスローされる

### `GetInfoAsync_ValidCall_PublishesCorrectEvent`
- **目的**: `GetInfoAsync` が正しい引数でイベントを発行することを確認
- **入力**:
  - ツール名: "test-tool"
  - ファイルパス: "test/path"
- **期待結果**:
  - イベントが発行される
  - イベント引数に正しい値が設定されている

### `GetInfoAsync_SubscriberSetsResult_ReturnsResult`
- **目的**: イベント購読側が結果を設定した場合、その結果が返ることを確認
- **入力**:
  - 期待結果: "test-info"
- **期待結果**:
  - 設定した結果が返る

### `GetInfoAsync_SubscriberSetsException_ThrowsException`
- **目的**: イベント購読側が例外を設定した場合、その例外がスローされることを確認
- **入力**:
  - 期待例外: Exception("Test error")
- **期待結果**:
  - 設定した例外がスローされる
