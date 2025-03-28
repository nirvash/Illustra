# MCP: ツール定義と呼び出しの仕様（Roo Code など共通）

Model Context Protocol (MCP) では、クライアントが API を呼び出す前に、
まずサーバーから利用可能な「ツール定義一覧（＝API一覧）」を取得し、
その後に個別のツールを引数付きで呼び出す流れになっています。

---

## ✅ ステップ1: ツール一覧取得（ListToolsRequest）

クライアントはサーバーに **`list_tools`** コマンドを送信し、
利用可能なツール一覧（`Tool[]`）を取得します。

### 🔸 サーバー側の応答形式

```json
{
  "tools": [
    {
      "name": "getStringLength",
      "description": "Get the length of a string",
      "inputSchema": {
        "type": "object",
        "properties": {
          "input": { "type": "string" }
        },
        "required": ["input"]
      }
    },
    ...
  ]
}
```

- 各ツールは `Tool` オブジェクトで定義
- `inputSchema` は JSON Schema 形式

---

## ✅ ステップ2: ツール呼び出し（CallToolRequest）

クライアントは任意のタイミングで、ツール名と引数を指定して呼び出しリクエストを送信します。

### 🔸 `POST /invoke` リクエスト例

```json
{
  "tool_name": "getStringLength",
  "arguments": {
    "input": "Hello"
  }
}
```

- `tool_name`: 実行するツール名（`Tool.name` と一致）
- `arguments`: JSON Schema に準拠した引数

---

## ✅ ステップ3: ツールの応答を SSE で返す

サーバーは `/events` エンドポイントから **SSE（Server-Sent Events）** でツールの実行結果を返します。

### 🔸 SSE フォーマット（`event: tool_response`）

```
event: tool_response
data: {
  "content": [
    { "type": "text", "text": "5" }
  ],
  "isError": false
}
```

- `content[]` はテキストや構造化データの配列（例：text, image, etc.）
- `isError` フラグで失敗時を示す

---

## ✅ 必須の流れまとめ

1. クライアントが接続し、`ListToolsRequest` でツール一覧を取得
2. ユーザー or AI によってツールが選択され、`CallToolRequest` で実行される
3. サーバーは `/events` に SSE で実行結果を push

---

## ✅ 補足：MCPサーバー実装がやること

- `ListToolsRequest` に対応：利用可能なツール一覧を返す
- `CallToolRequest` に対応：ツール呼び出しの処理とレスポンスを返す
- SSE接続中のクライアントに `tool_response` を送信する

この流れは Roo Code, Continue.dev などすべての MCP クライアントで共通です。
