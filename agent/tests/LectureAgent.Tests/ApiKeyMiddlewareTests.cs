using LectureAgent.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LectureAgent.Tests;

public class ApiKeyMiddlewareTests
{
    private static async Task<(int StatusCode, bool NextCalled)> InvokeAsync(string path, bool authEnabled, string? suppliedKey = null, System.Net.IPAddress? remoteIp = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Enabled"] = authEnabled.ToString(),
                ["Auth:ApiKey"] = "test-key-123",
                ["Agent:CenterId"] = "TestCenter"
            })
            .Build();

        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            config,
            NullLogger<ApiKeyMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (remoteIp != null)
        {
            context.Connection.RemoteIpAddress = remoteIp;
        }
        if (suppliedKey != null)
        {
            context.Request.Headers["X-Agent-Key"] = suppliedKey;
        }

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, nextCalled);
    }

    [Fact]
    public async Task DashboardShell_Assets_And_Health_LoadWithoutKey_WhenAuthEnabled()
    {
        var publicPaths = new[]
        {
            "/",
            "/index.html",
            "/assets/index-abc123.js",
            "/manifest.json",
            "/favicon.svg",
            "/health",
            "/api/health"
        };

        foreach (var path in publicPaths)
        {
            var (statusCode, nextCalled) = await InvokeAsync(path, authEnabled: true);
            Assert.True(nextCalled, $"Path {path} should pass through without a key");
            Assert.Equal(StatusCodes.Status200OK, statusCode);
        }
    }

    [Fact]
    public async Task ApiEndpoints_RequireKey_WhenAuthEnabled()
    {
        var (statusCode, nextCalled) = await InvokeAsync("/api/monitor/snapshot", authEnabled: true);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, statusCode);
    }

    [Fact]
    public async Task ApiEndpoints_AcceptCorrectKey_AndRejectWrongKey()
    {
        var ok = await InvokeAsync("/api/lectures", authEnabled: true, suppliedKey: "test-key-123");
        Assert.True(ok.NextCalled);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);

        var bad = await InvokeAsync("/api/lectures", authEnabled: true, suppliedKey: "wrong-key");
        Assert.False(bad.NextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, bad.StatusCode);
    }

    [Fact]
    public async Task Everything_PassesThrough_WhenAuthDisabled()
    {
        var result = await InvokeAsync("/api/lectures", authEnabled: false);
        Assert.True(result.NextCalled);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }

    [Fact]
    public async Task Loopback_PassesWithoutKey_EvenWhenAuthEnabled()
    {
        var result = await InvokeAsync("/api/monitor/snapshot", authEnabled: true, remoteIp: System.Net.IPAddress.Loopback);
        Assert.True(result.NextCalled);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }

    [Fact]
    public async Task RemoteLan_BlockedWithoutKey_EvenWhenAuthDisabled()
    {
        var lanIp = System.Net.IPAddress.Parse("192.168.1.55");
        var result = await InvokeAsync("/api/lectures", authEnabled: false, remoteIp: lanIp);
        Assert.False(result.NextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task RemoteLan_AllowedWithValidKey()
    {
        var lanIp = System.Net.IPAddress.Parse("192.168.1.55");
        var result = await InvokeAsync("/api/lectures", authEnabled: false, suppliedKey: "test-key-123", remoteIp: lanIp);
        Assert.True(result.NextCalled);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }
}
