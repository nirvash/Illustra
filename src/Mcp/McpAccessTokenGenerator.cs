using System;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace Illustra.Mcp
{
    /// <summary>
    /// MCP アクセストークンの生成ユーティリティ。
    /// 暗号学的に安全な乱数 32 バイト（256 ビット）を Base64Url エンコードした文字列を返す。
    /// </summary>
    internal static class McpAccessTokenGenerator
    {
        /// <summary>トークンの元となる乱数のバイト数。</summary>
        private const int TokenByteLength = 32;

        /// <summary>暗号学的に安全な乱数から Base64Url 形式のアクセストークンを生成する。</summary>
        public static string Generate()
        {
            return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenByteLength));
        }
    }
}
