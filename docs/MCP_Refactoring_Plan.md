# MCP 関連コードのリファクタリング計画

## 背景

`Illustra` プロジェクトと `Illustra.MCPHost` プロジェクト間で共有する必要のあるモデルクラス (`ToolExecuteRequest` など) が発生しました。
現在の構成では、`Illustra.csproj` が `src` ディレクトリ以下の `.cs` ファイルを再帰的に含んでしまうため、単純に `Illustra.MCPHost` プロジェクト内にモデルを配置するとビルドエラーが発生します。

## 目的

両プロジェクトでソースコードを共有し、ビルドエラーを解消するため、新しい共有プロジェクト `Illustra.Shared` を作成し、`ProjectReference` を用いて参照する構成に変更します。

## 作業手順

1.  **共有プロジェクトディレクトリ作成:** [x]
    *   `src/Shared` ディレクトリを作成します。
    *   `src/Shared/Models` ディレクトリを作成します。

2.  **共有プロジェクトファイル作成:** [x]
    *   `src/Shared/Illustra.Shared.csproj` を以下の内容で作成します。
      ```xml
      <Project Sdk="Microsoft.NET.Sdk">

        <PropertyGroup>
          <TargetFramework>net9.0</TargetFramework>
          <Nullable>enable</Nullable>
          <ImplicitUsings>enable</ImplicitUsings>
        </PropertyGroup>

        <ItemGroup>
          <PackageReference Include="System.Text.Json" Version="8.0.0" />
        </ItemGroup>

      </Project>
      ```

3.  **共有モデルクラス作成:** [x]
    *   `src/Shared/Models/ToolExecuteRequest.cs` を以下の内容で作成します。
      ```csharp
      using System.Text.Json.Serialization;

      namespace Illustra.Shared.Models
      {
          public class ToolExecuteRequest
          {
              [JsonPropertyName("parameters")]
              public object Parameters { get; set; }
          }
      }
      ```

4.  **プロジェクト参照の追加:** [x]
    *   `src/Illustra.csproj` に `Illustra.Shared.csproj` への `ProjectReference` を追加します。
    *   `src/MCPHost/Illustra.MCPHost.csproj` に `Illustra.Shared.csproj` への `ProjectReference` を追加します。

5.  **`Illustra.csproj` の修正:** [x]
    *   `Illustra.csproj` 内の `<Compile Remove="Shared\**\*.cs" />` 設定を確認し、正しく `src/Shared` ディレクトリを除外するようにします。（現在の設定 `Compile Remove="Shared\**\*.cs"` は正しいはずです）

6.  **`McpController.cs` の修正:** [x]
    *   `using` ディレクティブを `Illustra.Shared.Models` に修正します。
    *   `ExecuteTool` メソッドの引数の型を `ToolExecuteRequest` に修正します。

7.  **ソリューションファイルへの追加:** [x]
    *   `dotnet sln Illustra.sln add src/Shared/Illustra.Shared.csproj` コマンドを実行し、ソリューションに共有プロジェクトを追加します。

8.  **ビルド確認:** [x]
    *   ソリューション全体をビルドし、エラーが発生しないことを確認します。

9.  **API 動作再確認:** [x]
    *   アプリケーションをデバッグモードで起動します。
    *   curl や Postman を使用して、POST `/api/execute/test-tool` エンドポイントに以下の形式でリクエストを送信し、正常なレスポンスが返ってくることを確認します。
      ```
      curl -X POST http://localhost:5000/api/execute/test-tool \
        -H "Content-Type: application/json" \
        -d '{"parameters":{"param1":"value1","param2":"value2"}}'



10. **`McpController` の実装:**
    *   `ExecuteTool` メソッド内のプレースホルダーを、実際の `APIService` 呼び出しに置き換えます。
    *   必要に応じて、`GetInfo` メソッドも実装します。

11. **エラーハンドリングとログ:**
    *   `McpController` に、より詳細なエラーハンドリング (try-catch) とログ記録を追加します。
    *   `APIService` 側にも適切なエラー処理とログを実装します。

12. **デバッグコードの削除:**
    *   `Startup.cs` に追加したデバッグ用のログ出力を削除します。

13. **ビルド警告の修正:**
    *   `dotnet build` で報告された警告 (脆弱性、未使用コードなど) を可能な範囲で修正します。

14. **総合テスト:**
    *   複数のツール実行や情報取得シナリオで、MCP Host の動作を総合的にテストします。
