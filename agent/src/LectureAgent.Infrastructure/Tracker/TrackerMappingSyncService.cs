using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LectureAgent.Infrastructure.Tracker;

/// <summary>
/// Fetches the live room-to-batch mapping from the PW Center Tracker backend
/// (the same data shown in pw-center-tracker.betterpw.live). The endpoint is
/// public and reflects tracker edits immediately, so the agent can follow
/// mapping changes without anyone touching a spreadsheet.
/// </summary>
public sealed class TrackerMappingSyncService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<TrackerMappingSyncService> _logger;

    public TrackerMappingSyncService(HttpClient httpClient, IConfiguration config, ILogger<TrackerMappingSyncService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public bool IsEnabled =>
        !bool.TryParse(_config["TrackerMapping:Enabled"], out var enabled) || enabled;

    /// <summary>
    /// Returns batchName -> roomId for the given center, e.g. "27-AJ251NA 2026" -> "603".
    /// Rooms can host several batches; every batch of the center is included.
    /// </summary>
    public async Task<Dictionary<string, string>> GetBatchToRoomAsync(string centerId, CancellationToken ct = default)
    {
        var baseUrl = _config["TrackerMapping:BaseUrl"] ?? "https://pw-center-tracker-backend.betterpw.live/api/v1";
        var url = $"{baseUrl.TrimEnd('/')}/sheets/all?selectedCenter={Uri.EscapeDataString(centerId)}";

        var json = await _httpClient.GetStringAsync(url, ct);
        var mapping = ParseMapping(json);

        _logger.LogInformation("Tracker mapping loaded: {Count} batch(es) across {Rooms} room(s) for {Center}",
            mapping.Count, mapping.Values.Distinct().Count(), centerId);

        return mapping;
    }

    /// <summary>
    /// Parses the /sheets/all response shape:
    /// { data: [ { center, rooms: [ { room, batches: [ {}, { batchName, batchId }, ... ] } ] } ] }
    /// Empty batch placeholders ({} sent by the API as row separators) are skipped.
    /// </summary>
    public static Dictionary<string, string> ParseMapping(string json)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
        {
            var center = data[0];

            if (center.TryGetProperty("rooms", out var rooms) && rooms.ValueKind == JsonValueKind.Array)
            {
                foreach (var room in rooms.EnumerateArray())
                {
                    var roomName = room.TryGetProperty("room", out var r) ? r.GetString()?.Trim() : null;
                    if (string.IsNullOrWhiteSpace(roomName) || !room.TryGetProperty("batches", out var batches))
                        continue;

                    if (batches.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var batch in batches.EnumerateArray())
                    {
                        if (batch.ValueKind != JsonValueKind.Object ||
                            !batch.TryGetProperty("batchName", out var batchNameEl))
                        {
                            continue;
                        }

                        var batchName = batchNameEl.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(batchName))
                        {
                            mapping[batchName] = roomName;
                        }
                    }
                }
            }
        }

        return mapping;
    }
}
