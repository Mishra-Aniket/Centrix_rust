using System.Security.Cryptography;
using System.Text;
using LectureAgent.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LectureAgent.Security;

public sealed class ApiKeyMiddleware
{
    public const string DashboardSessionItem = "DashboardSession";
    public const string AuthModeItem = "AuthMode";

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
        if (IsPublicPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var authEnabled = _configuration.GetValue("Auth:Enabled", true);
        var requireLocalAuth = _configuration.GetValue("Auth:RequireAuthForLocalhost", false);
        var isLoopback = context.Connection.RemoteIpAddress != null && System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress);

        // Zone 1: Localhost / Loopback (Tauri Desktop App running on the recording PC)
        // Frictionless access without manual key configuration or login, unless explicitly forced.
        if (isLoopback && !requireLocalAuth)
        {
            context.Items[AuthModeItem] = "local";
            await _next(context);
            return;
        }

        // When auth is explicitly disabled in config, allow non-remote calls,
        // BUT if it's an external remote LAN caller, still protect port 5200 from unauthorized network tamper.
        if (!authEnabled && !IsRemoteLanRequest(context))
        {
            context.Items[AuthModeItem] = "key";
            await _next(context);
            return;
        }

        // Zone 2: Remote LAN / Center Wi-Fi or when Auth:Enabled is true
        // Check API Key or Session
        var configuredKey = _configuration["Auth:ApiKey"];
        var suppliedKey = context.Request.Headers["X-Agent-Key"].FirstOrDefault();

        // 1. Check API Key
        if (!string.IsNullOrWhiteSpace(configuredKey)
            && !string.IsNullOrWhiteSpace(suppliedKey)
            && CryptographicEquals(configuredKey, suppliedKey))
        {
            var configuredCenter = _configuration["Agent:CenterId"];
            var requestedCenter = context.Request.Headers["X-Center-Id"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(requestedCenter)
                && !string.Equals(configuredCenter, requestedCenter, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Center access denied" });
                return;
            }

            context.Items[AuthModeItem] = "key";
            await _next(context);
            return;
        }

        // 2. Check Dashboard Google Session
        var sessionToken = context.Request.Headers["X-Session"].FirstOrDefault() 
            ?? context.Request.Cookies["lasrs_session"];
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            var sessionService = context.RequestServices?.GetService<DashboardSessionService>();
            if (sessionService != null && sessionService.TryValidateSession(sessionToken, out var session) && session != null)
            {
                context.Items[DashboardSessionItem] = session;
                context.Items[AuthModeItem] = "google";
                await _next(context);
                return;
            }
        }

        _logger.LogWarning("Rejected unauthenticated request to {Path}", context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Authentication required" });
    }

    /// <summary>
    /// Everything outside /api is the static dashboard shell (html, js, css, icons) and
    /// carries no secrets, so it loads without a key and the login screen can render.
    /// /api/health and /api/auth/config, /api/auth/google/* stay public for sign-in.
    /// </summary>
    private static bool IsPublicPath(PathString path) =>
        !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/api/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/api/auth/config", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/api/auth/google", StringComparison.OrdinalIgnoreCase);

    private static bool IsRemoteLanRequest(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp == null)
            return false;

        if (System.Net.IPAddress.IsLoopback(remoteIp))
            return false;

        if (context.Connection.LocalIpAddress != null && remoteIp.Equals(context.Connection.LocalIpAddress))
            return false;

        return true;
    }

    private static bool CryptographicEquals(string expected, string supplied)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
