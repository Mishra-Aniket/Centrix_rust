namespace LectureAgent.Presentation.Controllers;

using System.Text;
using System.Text.Json;
using LectureAgent.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Center-level view: manages the list of room agents (each room PC runs its own
/// agent) and aggregates their live status into one overview for the admin.
/// Room list is stored in the local config table.
/// </summary>
[ApiController]
[Route("api/center")]
public sealed class CenterRoomsController : ControllerBase
{
    private const string ConfigKey = "CenterRooms";
    private readonly LectureContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public CenterRoomsController(LectureContext dbContext, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public sealed record RoomAgent
    {
        public string Name { get; init; } = null!;
        public string Url { get; init; } = null!;
        public string ApiKey { get; init; } = null!;
        public string RoomId { get; init; } = null!;
    }

    [HttpGet("rooms")]
    public async Task<ActionResult<List<RoomAgent>>> GetRooms()
    {
        return Ok(await LoadRoomsAsync());
    }

    [HttpPost("rooms")]
    public async Task<IActionResult> SaveRoom([FromBody] RoomAgent room)
    {
        if (string.IsNullOrWhiteSpace(room.Name) || string.IsNullOrWhiteSpace(room.Url))
            return BadRequest(new { error = "Name and Url are required" });

        var rooms = await LoadRoomsAsync();
        rooms.RemoveAll(r => string.Equals(r.Name, room.Name, StringComparison.OrdinalIgnoreCase));
        rooms.Add(new RoomAgent
        {
            Name = room.Name.Trim(),
            Url = room.Url.Trim().TrimEnd('/'),
            ApiKey = room.ApiKey?.Trim() ?? "",
            RoomId = room.RoomId?.Trim() ?? ""
        });

        await SaveRoomsAsync(rooms);
        return Ok(rooms);
    }

    [HttpDelete("rooms/{name}")]
    public async Task<IActionResult> DeleteRoom(string name)
    {
        var rooms = await LoadRoomsAsync();
        rooms.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        await SaveRoomsAsync(rooms);
        return Ok(rooms);
    }

    /// <summary>
    /// Live status of every configured room agent, fetched in parallel.
    /// A room that does not answer within 4 seconds shows as Down.
    /// </summary>
    [HttpGet("overview")]
    public async Task<ActionResult<List<RoomOverviewDto>>> GetOverview()
    {
        var rooms = await LoadRoomsAsync();
        var centerId = _configuration["Agent:CenterId"] ?? "";

        var tasks = rooms.Select(room => BuildRoomOverviewAsync(room, centerId));
        var overviews = await Task.WhenAll(tasks);

        return Ok(overviews.OrderBy(o => o.Name).ToList());
    }

    private async Task<RoomOverviewDto> BuildRoomOverviewAsync(RoomAgent room, string centerId)
    {
        var overview = new RoomOverviewDto
        {
            Name = room.Name,
            RoomId = room.RoomId,
            Url = room.Url
        };

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));

            var healthTask = GetJsonAsync($"{room.Url}/health", room.ApiKey, timeoutCts.Token);
            var snapshotTask = GetJsonAsync($"{room.Url}/api/monitor/snapshot", room.ApiKey, timeoutCts.Token);
            var missingTask = GetJsonAsync(
                $"{room.Url}/api/monitor/missing?centerId={Uri.EscapeDataString(centerId)}&roomId={Uri.EscapeDataString(room.RoomId)}",
                room.ApiKey, timeoutCts.Token);

            await Task.WhenAll(healthTask, snapshotTask, missingTask);

            overview.Live = healthTask.Result != null;

            if (snapshotTask.Result != null)
            {
                var snapshot = snapshotTask.Result.Value;
                if (snapshot.TryGetProperty("summary", out var summary))
                {
                    overview.Pending = summary.TryGetProperty("pending", out var p) ? p.GetInt32() : 0;
                    overview.Uploading = summary.TryGetProperty("uploading", out var u) ? u.GetInt32() : 0;
                    overview.Uploaded = summary.TryGetProperty("uploaded", out var up) ? up.GetInt32() : 0;
                    overview.Failed = summary.TryGetProperty("failed", out var f) ? f.GetInt32() : 0;
                    overview.TotalLectures = summary.TryGetProperty("totalLectures", out var t) ? t.GetInt32() : 0;
                }
            }

            if (missingTask.Result != null)
            {
                var missing = missingTask.Result.Value;
                if (missing.ValueKind == JsonValueKind.Array)
                {
                    overview.MissingCount = missing.GetArrayLength();
                    overview.MissingItems = missing.EnumerateArray()
                        .Take(3)
                        .Select(m => new MissingItemDto
                        {
                            Time = $"{GetTimeString(m, "slotStartTime")}-{GetTimeString(m, "slotEndTime")}",
                            Batch = m.TryGetProperty("batchId", out var b) ? b.GetString() ?? "" : "",
                            Subject = m.TryGetProperty("subjectId", out var s) ? s.GetString() ?? "" : ""
                        })
                        .ToList();
                }
            }
        }
        catch (Exception ex)
        {
            overview.Live = false;
            overview.Error = ex.Message;
        }

        return overview;
    }

    private static string GetTimeString(JsonElement obj, string propertyName)
    {
        if (obj.TryGetProperty(propertyName, out var element))
        {
            var value = element.GetString() ?? "";
            return value.Length >= 5 ? value[..5] : value;
        }
        return "";
    }

    private async Task<JsonElement?> GetJsonAsync(string url, string apiKey, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("CenterRooms");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Add("X-Agent-Key", apiKey);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<JsonElement>(content);
    }

    private async Task<List<RoomAgent>> LoadRoomsAsync()
    {
        var config = await _dbContext.Configs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConfigKey == ConfigKey);

        if (config == null || string.IsNullOrWhiteSpace(config.ConfigValue))
            return new List<RoomAgent>();

        try
        {
            return JsonSerializer.Deserialize<List<RoomAgent>>(config.ConfigValue!) ?? new List<RoomAgent>();
        }
        catch
        {
            return new List<RoomAgent>();
        }
    }

    private async Task SaveRoomsAsync(List<RoomAgent> rooms)
    {
        var json = JsonSerializer.Serialize(rooms, new JsonSerializerOptions { WriteIndented = true });

        var config = await _dbContext.Configs.FirstOrDefaultAsync(c => c.ConfigKey == ConfigKey);
        if (config == null)
        {
            config = new Domain.Entities.ConfigEntry
            {
                ConfigKey = ConfigKey,
                ConfigValue = json,
                DataType = "JSON"
            };
            await _dbContext.Configs.AddAsync(config);
        }
        else
        {
            config.ConfigValue = json;
            config.LastUpdated = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();
    }
}

public sealed class RoomOverviewDto
{
    public string Name { get; set; } = null!;
    public string RoomId { get; set; } = "";
    public string Url { get; set; } = null!;
    public bool Live { get; set; }
    public string? Error { get; set; }
    public int Pending { get; set; }
    public int Uploading { get; set; }
    public int Uploaded { get; set; }
    public int Failed { get; set; }
    public int TotalLectures { get; set; }
    public int MissingCount { get; set; }
    public List<MissingItemDto> MissingItems { get; set; } = new();
}

public sealed class MissingItemDto
{
    public string Time { get; set; } = "";
    public string Batch { get; set; } = "";
    public string Subject { get; set; } = "";
}
