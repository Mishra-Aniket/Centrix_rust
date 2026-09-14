using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LectureAgent.Desktop;

internal sealed record AgentSnapshot(
    string CenterId,
    string RoomId,
    string Version,
    TimeSpan Uptime,
    bool UploadsPaused,
    string MonitorFolder,
    IReadOnlyList<string> LanAddresses,
    int Pending,
    int Uploading,
    int Failed,
    int Uploaded);

/// <summary>
/// One poll result: either a live snapshot, a definitive "key rejected" (401), or nothing
/// reachable at all. The window shows a different screen for each case.
/// </summary>
internal sealed record AgentProbe(AgentSnapshot? Snapshot, bool Unauthorized)
{
    internal static readonly AgentProbe Unreachable = new(null, Unauthorized: false);
}

/// <summary>
/// Talks to the local agent's HTTP API for the status shown in the window header and the
/// tray menu, and issues the control actions those menus offer.
/// </summary>
internal sealed class AgentApiClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    internal async Task<AgentProbe> ProbeAsync(AgentSettings settings)
    {
        HttpResponseMessage infoResponse;
        try
        {
            infoResponse = await SendAsync(HttpMethod.Get, settings, "/api/agent/info");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return AgentProbe.Unreachable;
        }

        using (infoResponse)
        {
            if (infoResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new AgentProbe(null, Unauthorized: true);
            }

            if (!infoResponse.IsSuccessStatusCode)
            {
                return AgentProbe.Unreachable;
            }

            try
            {
                return new AgentProbe(await ReadSnapshotAsync(settings, infoResponse), Unauthorized: false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return AgentProbe.Unreachable;
            }
        }
    }

    private async Task<AgentSnapshot?> ReadSnapshotAsync(AgentSettings settings, HttpResponseMessage infoResponse)
    {
        using var infoJson = JsonDocument.Parse(await infoResponse.Content.ReadAsStringAsync());
        var info = infoJson.RootElement;

            var addresses = new List<string>();
            if (info.TryGetProperty("lanIpv4Addresses", out var addressArray)
                && addressArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var address in addressArray.EnumerateArray())
                {
                    if (address.GetString() is { Length: > 0 } text)
                    {
                        addresses.Add(text);
                    }
                }
            }

            var pending = 0;
            var uploading = 0;
            var failed = 0;
            var uploaded = 0;

            using var snapshotResponse = await SendAsync(HttpMethod.Get, settings, "/api/monitor/snapshot");
            if (snapshotResponse.IsSuccessStatusCode)
            {
                using var snapshotJson = JsonDocument.Parse(await snapshotResponse.Content.ReadAsStringAsync());
                if (snapshotJson.RootElement.TryGetProperty("summary", out var summary))
                {
                    pending = ReadInt(summary, "pending");
                    uploading = ReadInt(summary, "uploading");
                    failed = ReadInt(summary, "failed");
                    uploaded = ReadInt(summary, "uploaded");
                }
            }

            return new AgentSnapshot(
                ReadString(info, "centerId"),
                ReadString(info, "roomId"),
                ReadString(info, "agentVersion"),
                TimeSpan.FromSeconds(info.TryGetProperty("uptimeSeconds", out var uptime) ? uptime.GetInt64() : 0),
                info.TryGetProperty("uploadsPaused", out var paused) && paused.GetBoolean(),
                ReadString(info, "monitorFolder"),
                addresses,
                pending,
                uploading,
                failed,
                uploaded);
    }

    internal async Task<bool> PostAsync(AgentSettings settings, string path)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, settings, path);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, AgentSettings settings, string path)
    {
        var request = new HttpRequestMessage(method, settings.LocalBaseUrl + path);
        request.Headers.TryAddWithoutValidation("X-Agent-Key", settings.ApiKey);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return _http.SendAsync(request);
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;

    public void Dispose() => _http.Dispose();
}
