# MCP HTTP サーバー仕様（クライアント共通）

Model Context Protocol (MCP) に対応した HTTP サーバーは、以下の 3 つのエンドポイントを提供する必要があります。  
これは Roo Code を含む全 MCP 対応クライアントで共通の仕様です。

---

## ✅ 必須エンドポイント

### `POST /start`
- クライアント起動時の初期通知。
- リクエストボディは空で構いません。
- レスポンスも空で OK（HTTP 200）。

---

### `POST /invoke`
- クライアントからのツール呼び出し。
- JSON ボディに `tool_name` と `arguments` を含みます。
- サーバーは結果を `/events` に送信します。

#### リクエスト例

```json
{
  "tool_name": "getStringLength",
  "arguments": {
    "input": "Hello"
  }
}
```

---

### `GET /events`
- サーバーからクライアントへのリアルタイム応答用。
- **SSE (Server-Sent Events)** を使用。
- `Content-Type: text/event-stream` を返します。

#### レスポンス例

```
event: tool_response
data: {
  "content": [
    { "type": "text", "text": "5" }
  ],
  "isError": false
}
```

---

## 🌍 エンドポイントのベースURLについて

`.mcpSettings.json` に以下のように `url` を指定します：

```json
{
  "mcpServers": {
    "MyServer": {
      "url": "http://localhost:5149",
      "disabled": false,
      "alwaysAllow": []
    }
  }
}
```

この場合、アクセスされるのは以下の3つ：

- `http://localhost:5149/start`
- `http://localhost:5149/invoke`
- `http://localhost:5149/events`

---

## ⚠️ 注意点

- `/events` は `text/event-stream` 形式の SSE を返す必要があります。
- すべてのエンドポイントはベースURLに対する **固定の相対パス** です。
- `/api/start` や `/mcp/invoke` のようなカスタムパスは使用できません。

---

## ✅ この仕様はすべての MCP クライアントに共通

- Roo Code
- Continue.dev
- 将来的な他の MCP 対応ツール

すべてがこの 3 エンドポイントを使ってサーバーと通信します。
