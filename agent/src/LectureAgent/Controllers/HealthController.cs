namespace LectureAgent.Presentation.Controllers;

using LectureAgent.Domain.Services;
using LectureAgent.Domain.Enums;
using LectureAgent.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Health check and status endpoint.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly LectureContext _dbContext;
    private readonly IFileWatcher _fileWatcher;
    private readonly ILogger<HealthController> _logger;

    // Alias the root health endpoint so it works without the API prefix.
    [HttpGet("/health")]
    public Task<ActionResult<HealthStatusDto>> GetHealthRoot() => GetHealth();

    public HealthController(
        LectureContext dbContext,
        IFileWatcher fileWatcher,
        ILogger<HealthController> logger)
    {
        _dbContext = dbContext;
        _fileWatcher = fileWatcher;
        _logger = logger;
    }

    /// <summary>
    /// Returns overall system health.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<HealthStatusDto>> GetHealth()
    {
        var status = new HealthStatusDto
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow
        };

        try
        {
            // Check database
            var canConnect = await _dbContext.Database.CanConnectAsync();
            status.Database = canConnect ? "Connected" : "Disconnected";
            if (!canConnect)
                status.Status = "Unhealthy";
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Database health check failed: {ex.Message}");
            status.Database = $"Error: {ex.Message}";
            status.Status = "Unhealthy";
        }

        // Check file watcher
        status.FileWatcherActive = _fileWatcher.IsRunning;

        // Get queue statistics
        try
        {
            var totalLectures = await _dbContext.LectureSessions.CountAsync();
            var pendingReview = await _dbContext.LectureSessions
                .CountAsync(l => l.Status == LectureStatus.ReviewRequired);
            var queued = await _dbContext.UploadQueues
                .CountAsync(u => u.Status == UploadStatus.Pending);
            var failed = await _dbContext.UploadQueues
                .CountAsync(u => u.Status == UploadStatus.Failed);

            status.Statistics = new
            {
                TotalLectures = totalLectures,
                PendingReview = pendingReview,
                QueuedUploads = queued,
                FailedUploads = failed
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Statistics retrieval failed: {ex.Message}");
        }

        var statusCode = status.Status == "Healthy" ? 200 : 503;
        return StatusCode(statusCode, status);
    }
}

/// <summary>
/// DTO for health status.
/// </summary>
public class HealthStatusDto
{
    public string Status { get; set; } = null!;
    public DateTime Timestamp { get; set; }
    public string? Database { get; set; }
    public bool FileWatcherActive { get; set; }
    public object? Statistics { get; set; }
}
