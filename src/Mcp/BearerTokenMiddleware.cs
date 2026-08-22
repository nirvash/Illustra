using Microsoft.AspNetCore.Http;

namespace Illustra.Mcp
{
    /// <summary>
    /// /mcp エンドポイントへのリクエストに対して Bearer トークン認証を要求するミドルウェア。
    /// </summary>
    public class BearerTokenMiddleware
    {
        private const string McpPath = "/mcp";
        private readonly RequestDelegate _next;
        private readonly Func<string?> _tokenProvider;

        public BearerTokenMiddleware(RequestDelegate next, Func<string?> tokenProvider)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments(McpPath))
            {
                var expectedToken = _tokenProvider();

                // トークン未設定の場合は拒否（設定不備を明確化）
                if (string.IsNullOrEmpty(expectedToken))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsync("MCP access token is not configured.");
                    return;
                }

                var authHeader = context.Request.Headers.Authorization.ToString();
                const string BearerPrefix = "Bearer ";

                if (!authHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(authHeader[BearerPrefix.Length..].Trim(), expectedToken, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await context.Response.WriteAsync("Unauthorized.");
                    return;
                }
            }

            await _next(context);
        }
    }
}
