using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LectureAgent.Infrastructure.StudioApi;

public sealed class StudioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<StudioApiClient> _logger;

    public StudioApiClient(HttpClient httpClient, IConfiguration config, ILogger<StudioApiClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public bool IsEnabled =>
        bool.TryParse(_config["StudioApi:Enabled"], out var enabled) && enabled;

    private string BaseUrl => _config["StudioApi:BaseUrl"] ?? "https://studio-app-api.penpencil.co/v1/studio";
    private string? IdToken => _config["StudioApi:IdToken"];

    private HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
        var token = IdToken;
        if (!string.IsNullOrEmpty(token))
        {
            // PenPencil Studio API strictly requires the raw JWT token without the "Bearer " prefix
            request.Headers.TryAddWithoutValidation("Authorization", token.Trim());
        }
        return request;
    }

    /// Fetches /sheets/master - returns all Centers, Rooms, Batches
    public async Task<List<StudioMasterEntry>> GetMasterSheetsAsync(CancellationToken ct = default)
    {
        var url = $"{BaseUrl.TrimEnd('/')}/sheets/master";
        _logger.LogInformation("Fetching Studio API master sheets from {Url}", url);

        var request = CreateRequest(url);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        var entries = new List<StudioMasterEntry>();
        var isFirstRow = true;

        foreach (var row in data.EnumerateArray())
        {
            if (isFirstRow) { isFirstRow = false; continue; } // Skip header row
            if (row.ValueKind != JsonValueKind.Array) continue;

            var cols = new List<string>();
            foreach (var col in row.EnumerateArray())
                cols.Add(col.GetString() ?? "");

            if (cols.Count >= 2)
            {
                entries.Add(new StudioMasterEntry
                {
                    Center = cols.Count > 0 ? cols[0] : "",
                    Room = cols.Count > 1 ? cols[1] : "",
                    BatchName = cols.Count > 2 ? cols[2] : "",
                    BatchId = cols.Count > 3 ? cols[3] : ""
                });
            }
        }

        _logger.LogInformation("Studio API master sheets loaded: {Count} entries", entries.Count);
        return entries;
    }

    /// Fetches /sheets/teacher - returns all Teachers with Drive IDs
    public async Task<List<StudioTeacherEntry>> GetTeacherSheetsAsync(CancellationToken ct = default)
    {
        var url = $"{BaseUrl.TrimEnd('/')}/sheets/teacher";
        _logger.LogInformation("Fetching Studio API teacher sheets from {Url}", url);

        var request = CreateRequest(url);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        var entries = new List<StudioTeacherEntry>();
        var isFirstRow = true;

        foreach (var row in data.EnumerateArray())
        {
            if (isFirstRow) { isFirstRow = false; continue; }
            if (row.ValueKind != JsonValueKind.Array) continue;

            var cols = new List<string>();
            foreach (var col in row.EnumerateArray())
                cols.Add(col.GetString() ?? "");

            if (cols.Count >= 2)
            {
                entries.Add(new StudioTeacherEntry
                {
                    Center = cols.Count > 0 ? cols[0] : "",
                    Name = cols.Count > 1 ? cols[1] : "",
                    Email = cols.Count > 2 ? cols[2] : "",
                    DriveLink = cols.Count > 3 ? cols[3] : "",
                    DriveId = cols.Count > 4 ? cols[4] : ""
                });
            }
        }

        _logger.LogInformation("Studio API teacher sheets loaded: {Count} entries", entries.Count);
        return entries;
    }

    /// Fetches /files - returns live lecture files with filters
    public async Task<List<StudioFileEntry>> GetFilesAsync(
        string[]? centers = null, string? room = null,
        string? from = null, string? to = null,
        string? fileType = null, string? batchId = null,
        CancellationToken ct = default)
    {
        var queryParts = new List<string> { "includeThumbnailStatus=1" };
        if (from != null) queryParts.Add($"from={Uri.EscapeDataString(from)}");
        if (to != null) queryParts.Add($"to={Uri.EscapeDataString(to)}");
        if (centers != null && centers.Length > 0)
            queryParts.Add($"centers={Uri.EscapeDataString(JsonSerializer.Serialize(centers))}");
        if (!string.IsNullOrEmpty(room)) queryParts.Add($"room={Uri.EscapeDataString(room)}");
        if (!string.IsNullOrEmpty(fileType)) queryParts.Add($"type={Uri.EscapeDataString(fileType)}");
        if (!string.IsNullOrEmpty(batchId)) queryParts.Add($"batchId={Uri.EscapeDataString(batchId)}");

        var url = $"{BaseUrl.TrimEnd('/')}/files?{string.Join("&", queryParts)}";
        _logger.LogInformation("Fetching Studio API files from {Url}", url);

        var request = CreateRequest(url);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<StudioApiResponse<List<StudioFileEntry>>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var files = result?.Data ?? new List<StudioFileEntry>();
        _logger.LogInformation("Studio API files loaded: {Count} entries", files.Count);
        return files;
    }

    /// Pings the Studio API to check connectivity and token validity
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl.TrimEnd('/')}/ping";
            var request = CreateRequest(url);
            var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Studio API ping failed");
            return false;
        }
    }
}
