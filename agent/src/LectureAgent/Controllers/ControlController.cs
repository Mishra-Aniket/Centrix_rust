namespace LectureAgent.Presentation.Controllers;

using LectureAgent.Application.Services;
using LectureAgent.Domain.Services;
using LectureAgent.Services;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Runtime control endpoints for the dashboard: pause/resume background work,
/// retry or cancel uploads, rescan the monitored folder, and force a timetable sync.
/// </summary>
[ApiController]
[Route("api/control")]
public sealed class ControlController : ControllerBase
{
    private readonly AgentControlState _controlState;
    private readonly UploadQueueService _queueService;
    private readonly TimetableSyncService _timetableSyncService;
    private readonly IFileWatcher _fileWatcher;
    private readonly IAuditLogger _auditLogger;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ControlController> _logger;

    public ControlController(
        AgentControlState controlState,
        UploadQueueService queueService,
        TimetableSyncService timetableSyncService,
        IFileWatcher fileWatcher,
        IAuditLogger auditLogger,
        IConfiguration configuration,
        ILogger<ControlController> logger)
    {
        _controlState = controlState;
        _queueService = queueService;
        _timetableSyncService = timetableSyncService;
        _fileWatcher = fileWatcher;
        _auditLogger = auditLogger;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Current state of all runtime switches.
    /// </summary>
    [HttpGet("state")]
    public ActionResult<ControlStateDto> GetState()
    {
        return Ok(ControlStateDto.From(_controlState));
    }

    [HttpPost("uploads/pause")]
    public async Task<IActionResult> PauseUploads()
    {
        _controlState.SetUploadsPaused(true);
        await LogControlAction("UPLOADS_PAUSED", "Upload processing paused from dashboard");
        return Ok(ControlStateDto.From(_controlState));
    }

    [HttpPost("uploads/resume")]
    public async Task<IActionResult> ResumeUploads()
    {
        _controlState.SetUploadsPaused(false);
        await LogControlAction("UPLOADS_RESUMED", "Upload processing resumed from dashboard");
        return Ok(ControlStateDto.From(_controlState));
    }

    [HttpPost("uploads/retry-failed")]
    public async Task<IActionResult> RetryFailedUploads()
    {
        var count = await _queueService.RetryAllFailedAsync();
        return Ok(new { retried = count });
    }

    [HttpPost("uploads/{queueEntryId}/retry")]
    public async Task<IActionResult> RetryUpload(string queueEntryId)
    {
        try
        {
            var entry = await _queueService.RetryAsync(queueEntryId);
            return Ok(entry);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("uploads/{queueEntryId}/cancel")]
    public async Task<IActionResult> CancelUpload(string queueEntryId)
    {
        try
        {
            var entry = await _queueService.CancelAsync(queueEntryId);
            return Ok(entry);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("monitoring/pause")]
    public async Task<IActionResult> PauseMonitoring()
    {
        _controlState.SetMonitoringPaused(true);
        await LogControlAction("MONITORING_PAUSED", "File monitoring paused from dashboard");
        return Ok(ControlStateDto.From(_controlState));
    }

    [HttpPost("monitoring/resume")]
    public async Task<IActionResult> ResumeMonitoring()
    {
        _controlState.SetMonitoringPaused(false);
        await LogControlAction("MONITORING_RESUMED", "File monitoring resumed from dashboard");
        return Ok(ControlStateDto.From(_controlState));
    }

    /// <summary>
    /// Re-enumerates the monitored folder. Files that already have a lecture session
    /// are skipped by the processing pipeline, so this is safe to repeat.
    /// </summary>
    [HttpPost("monitoring/rescan")]
    public async Task<IActionResult> RescanFolder()
    {
        if (!_fileWatcher.IsRunning)
            return Conflict(new { error = "File watcher is not running" });

        var tracked = _fileWatcher.ScanExisting();
        _logger.LogInformation($"Manual rescan tracked {tracked} new file(s)");
        await LogControlAction("FOLDER_RESCAN", $"Manual rescan found {tracked} new file(s)");
        return Ok(new { newlyTracked = tracked });
    }

    /// <summary>
    /// Triggers the Google Sheet timetable sync immediately. Note that the sheet is
    /// the source of truth: slots for the configured room are replaced by sheet data.
    /// </summary>
    [HttpPost("timetable/sync")]
    public async Task<IActionResult> SyncTimetable()
    {
        if (_controlState.TimetableSyncPaused)
            return Conflict(new { error = "Timetable sync is paused; resume it first" });

        var centerId = _configuration["Agent:CenterId"] ?? "UNKNOWN";
        await _timetableSyncService.SyncTimetableAsync(centerId);
        await LogControlAction("TIMETABLE_SYNC", $"Manual timetable sync completed for {centerId}");
        return Ok(new { synced = true, centerId });
    }

    [HttpPost("timetable/sync/pause")]
    public async Task<IActionResult> PauseTimetableSync()
    {
        _controlState.SetTimetableSyncPaused(true);
        await LogControlAction("TIMETABLE_SYNC_PAUSED", "Timetable sheet sync paused from dashboard");
        return Ok(ControlStateDto.From(_controlState));
    }

    [HttpPost("timetable/sync/resume")]
    public async Task<IActionResult> ResumeTimetableSync()
    {
        _controlState.SetTimetableSyncPaused(false);
        await LogControlAction("TIMETABLE_SYNC_RESUMED", "Timetable sheet sync resumed from dashboard");
        return Ok(ControlStateDto.From(_controlState));
    }

    private async Task LogControlAction(string actionType, string summary)
    {
        _logger.LogInformation(summary);
        await _auditLogger.LogAsync("AGENT_CONTROL", "AGENT", actionType, "dashboard",
            null, ControlStateDto.From(_controlState), summary);
    }
}

/// <summary>
/// Snapshot of the agent runtime switches, also used as the response body of
/// pause/resume endpoints so the UI can update from a single source.
/// </summary>
public sealed class ControlStateDto
{
    public bool UploadsPaused { get; set; }
    public DateTime? UploadsPausedAt { get; set; }
    public bool MonitoringPaused { get; set; }
    public DateTime? MonitoringPausedAt { get; set; }
    public bool TimetableSyncPaused { get; set; }
    public DateTime? TimetableSyncPausedAt { get; set; }

    public static ControlStateDto From(AgentControlState state) => new()
    {
        UploadsPaused = state.UploadsPaused,
        UploadsPausedAt = state.UploadsPausedAt,
        MonitoringPaused = state.MonitoringPaused,
        MonitoringPausedAt = state.MonitoringPausedAt,
        TimetableSyncPaused = state.TimetableSyncPaused,
        TimetableSyncPausedAt = state.TimetableSyncPausedAt
    };
}
