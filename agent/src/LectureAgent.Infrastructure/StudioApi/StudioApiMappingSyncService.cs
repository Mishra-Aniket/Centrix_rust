using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LectureAgent.Infrastructure.StudioApi;

/// <summary>
/// Provides batch-to-room and teacher-to-drive mappings from the PW Studio API
/// (/sheets/master and /sheets/teacher). Designed as a higher-priority alternative
/// to TrackerMappingSyncService in the fallback chain.
/// </summary>
public sealed class StudioApiMappingSyncService
{
    private readonly StudioApiClient _client;
    private readonly IConfiguration _config;
    private readonly ILogger<StudioApiMappingSyncService> _logger;

    public StudioApiMappingSyncService(StudioApiClient client, IConfiguration config, ILogger<StudioApiMappingSyncService> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    public bool IsEnabled => _client.IsEnabled;

    /// <summary>
    /// Returns batchName -> roomId for the given center from /sheets/master.
    /// </summary>
    public async Task<Dictionary<string, string>> GetBatchToRoomAsync(string centerId, CancellationToken ct = default)
    {
        var masterEntries = await _client.GetMasterSheetsAsync(ct);
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in masterEntries)
        {
            if (string.Equals(entry.Center, centerId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(entry.BatchName)
                && !string.IsNullOrWhiteSpace(entry.Room))
            {
                mapping[entry.BatchName] = entry.Room;
            }
        }

        _logger.LogInformation(
            "Studio API mapping loaded: {Count} batch(es) across {Rooms} room(s) for {Center}",
            mapping.Count, mapping.Values.Distinct().Count(), centerId);

        return mapping;
    }

    /// <summary>
    /// Returns teacherName -> StudioTeacherEntry for the given center from /sheets/teacher.
    /// </summary>
    public async Task<Dictionary<string, StudioTeacherEntry>> GetTeacherDriveMapAsync(string centerId, CancellationToken ct = default)
    {
        var teacherEntries = await _client.GetTeacherSheetsAsync(ct);
        var mapping = new Dictionary<string, StudioTeacherEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in teacherEntries)
        {
            if (string.Equals(entry.Center, centerId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(entry.Name))
            {
                mapping[entry.Name] = entry;
            }
        }

        _logger.LogInformation(
            "Studio API teacher mapping loaded: {Count} teacher(s) for {Center}",
            mapping.Count, centerId);

        return mapping;
    }
}
