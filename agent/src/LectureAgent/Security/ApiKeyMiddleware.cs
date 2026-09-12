using System.Security.Cryptography;
using System.Text;

namespace LectureAgent.Security;

public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_configuration.GetValue("Auth:Enabled", false)
            || IsPublicPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var configuredKey = _configuration["Auth:ApiKey"];
        var suppliedKey = context.Request.Headers["X-Agent-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(configuredKey)
            || string.IsNullOrWhiteSpace(suppliedKey)
            || !CryptographicEquals(configuredKey, suppliedKey))
        {
            _logger.LogWarning("Rejected unauthenticated request to {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Authentication required" });
            return;
        }

        var configuredCenter = _configuration["Agent:CenterId"];
        var requestedCenter = context.Request.Headers["X-Center-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(requestedCenter)
            && !string.Equals(configuredCenter, requestedCenter, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Center access denied" });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Everything outside /api is the static dashboard shell (html, js, css, icons) and
    /// carries no secrets, so it loads without a key and the login screen can render.
    /// /api/health stays public for uptime checks; every other API call needs the key.
    /// </summary>
    private static bool IsPublicPath(PathString path) =>
        !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/api/health", StringComparison.OrdinalIgnoreCase);

    private static bool CryptographicEquals(string expected, string supplied)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
