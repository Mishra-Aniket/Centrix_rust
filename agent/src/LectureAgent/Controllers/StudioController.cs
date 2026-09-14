namespace LectureAgent.Presentation.Controllers;

using LectureAgent.Infrastructure.StudioApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Proxies PW Studio API data (centers, rooms, batches, teachers, live files)
/// for the Centrix web dashboard.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class StudioController : ControllerBase
{
    private readonly StudioApiClient _studioClient;
    private readonly IConfiguration _config;
    private readonly ILogger<StudioController> _logger;

    public StudioController(StudioApiClient studioClient, IConfiguration config, ILogger<StudioController> logger)
    {
        _studioClient = studioClient;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Studio API connectivity and token validity check.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        if (!_studioClient.IsEnabled)
            return Ok(new { connected = false, reason = "Studio API is disabled in configuration" });

        var reachable = await _studioClient.PingAsync();
        return Ok(new { connected = reachable, baseUrl = _config["StudioApi:BaseUrl"] ?? "" });
    }

    /// <summary>
    /// Updates the PW Studio ID token dynamically from the UI and verifies connectivity.
    /// </summary>
    [HttpPost("token")]
    public async Task<IActionResult> UpdateToken([FromBody] UpdateStudioTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Token))
            return BadRequest(new { error = "Token cannot be empty" });

        var token = request.Token.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token[7..].Trim();

        _config["StudioApi:IdToken"] = token;
        _config["StudioApi:Enabled"] = "true";

        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (System.IO.File.Exists(configPath))
            {
                var json = await System.IO.File.ReadAllTextAsync(configPath);
                var updatedJson = System.Text.RegularExpressions.Regex.Replace(
                    json,
                    "\"IdToken\"\\s*:\\s*\"[^\"]*\"",
                    $"\"IdToken\": \"{token}\"");
                await System.IO.File.WriteAllTextAsync(configPath, updatedJson);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist token to appsettings.json file");
        }

        var isConnected = await _studioClient.PingAsync();
        return Ok(new { success = true, connected = isConnected });
    }

    /// <summary>
    /// All centers, rooms, and batches from /sheets/master.
    /// </summary>
    [HttpGet("centers")]
    public async Task<IActionResult> GetCenters()
    {
        if (!_studioClient.IsEnabled)
            return BadRequest(new { error = "Studio API is not enabled" });

        try
        {
            var entries = await _studioClient.GetMasterSheetsAsync();

            // Group by center for easier consumption
            var grouped = entries
                .GroupBy(e => e.Center)
                .Select(g => new
                {
                    center = g.Key,
                    rooms = g.Select(e => e.Room).Where(r => !string.IsNullOrEmpty(r)).Distinct().ToList(),
                    batches = g.Select(e => new { batchName = e.BatchName, batchId = e.BatchId })
                              .Where(b => !string.IsNullOrEmpty(b.batchName))
                              .Distinct().ToList()
                })
                .OrderBy(c => c.center)
                .ToList();

            return Ok(new { total = entries.Count, centers = grouped });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch Studio API centers");
            return StatusCode(502, new { error = $"Studio API error: {ex.Message}" });
        }
    }

    /// <summary>
    /// Teachers with Drive IDs for the configured center (or specified center).
    /// </summary>
    [HttpGet("teachers")]
    public async Task<IActionResult> GetTeachers([FromQuery] string? centerId = null)
    {
        if (!_studioClient.IsEnabled)
            return BadRequest(new { error = "Studio API is not enabled" });

        try
        {
            var center = centerId ?? _config["Agent:CenterId"] ?? "";
            var entries = await _studioClient.GetTeacherSheetsAsync();

            var filtered = string.IsNullOrEmpty(center)
                ? entries
                : entries.Where(e => string.Equals(e.Center, center, StringComparison.OrdinalIgnoreCase)).ToList();

            return Ok(new { total = filtered.Count, center, teachers = filtered });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch Studio API teachers");
            return StatusCode(502, new { error = $"Studio API error: {ex.Message}" });
        }
    }

    /// <summary>
    /// Live lecture files from /files with optional filters.
    /// </summary>
    [HttpGet("files")]
    public async Task<IActionResult> GetFiles(
        [FromQuery] string? center = null,
        [FromQuery] string? room = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] string? fileType = null,
        [FromQuery] string? batchId = null)
    {
        if (!_studioClient.IsEnabled)
            return BadRequest(new { error = "Studio API is not enabled" });

        try
        {
            var centers = !string.IsNullOrWhiteSpace(center) ? new[] { center.Trim() } : null;

            var files = await _studioClient.GetFilesAsync(centers, room, from, to, fileType, batchId);
            return Ok(new { total = files.Count, files });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch Studio API files");
            return StatusCode(502, new { error = $"Studio API error: {ex.Message}" });
        }
    }
}

public sealed class UpdateStudioTokenRequest
{
    public string Token { get; set; } = "";
}
