using Microsoft.AspNetCore.Http;

namespace Server.AdminApi;

public class SessionKeyMiddleware
{
    public const string HeaderName = "X-Session-Key";
    public const string UserIdItemKey = "AdminUserId";

    private static readonly HashSet<string> _exemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/health",
        "/api/auth/login",
        "/swagger",
        "/swagger/index.html",
        "/swagger/v1/swagger.json"
    };

    private readonly RequestDelegate _next;

    public SessionKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, SessionKeyStore store)
    {
        var path = context.Request.Path.Value ?? "";
        if (IsExempt(path))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var keys) || keys.Count == 0)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync($"missing {HeaderName} header");
            return;
        }

        var userId = await store.ValidateAsync(keys[0]!);
        if (userId == null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("invalid or expired session key");
            return;
        }

        context.Items[UserIdItemKey] = userId;
        await _next(context);
    }

    private static bool IsExempt(string path)
    {
        if (_exemptPaths.Contains(path)) return true;
        if (path.StartsWith("/swagger/", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
