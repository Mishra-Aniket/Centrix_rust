namespace LectureAgent.Services;

using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using LectureAgent.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Automatic storage manager that frees disk space by deleting local video/PDF recordings
/// that have already been fully uploaded and verified on Google Drive after a retention period.
/// Prevents the 20 classroom computers from running out of disk space during high recording volumes.
/// </summary>
public sealed class StorageCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StorageCleanupService> _logger;

    public StorageCleanupService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<StorageCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay on agent start (3 minutes)
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Storage cleanup cycle encountered an error; will retry on next schedule");
            }

            // Run every 6 hours
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    internal async Task<long> RunCleanupCycleAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = _configuration.GetValue("StorageCleanup:RetentionDays", 14);
        var minFreeGigabytes = _configuration.GetValue("StorageCleanup:MinFreeDiskGigabytes", 15);
        var enabled = _configuration.GetValue("StorageCleanup:Enabled", true);

        if (!enabled)
        {
            return 0;
        }

        long bytesReclaimed = 0;
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LectureContext>();

        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

        // Find lectures that are fully uploaded and older than retention period
        var candidates = await db.LectureSessions
            .Where(l => (l.Status == LectureStatus.Uploaded || l.Status == LectureStatus.Verified || l.Status == LectureStatus.Archived)
                && l.CreatedAt <= cutoffDate
                && (!string.IsNullOrEmpty(l.VideoFileLocalPath) || !string.IsNullOrEmpty(l.PdfFileLocalPath)))
            .OrderBy(l => l.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var lecture in candidates)
        {
            // Verify that an upload queue entry exists and is successfully Uploaded
            var isVideoUploaded = string.IsNullOrEmpty(lecture.VideoFileLocalPath) ||
                await db.UploadQueues.AnyAsync(q => q.LectureSessionId == lecture.LectureSessionId && q.FileType == "VIDEO" && q.Status == UploadStatus.Uploaded, cancellationToken);

            var isPdfUploaded = string.IsNullOrEmpty(lecture.PdfFileLocalPath) ||
                await db.UploadQueues.AnyAsync(q => q.LectureSessionId == lecture.LectureSessionId && q.FileType == "PDF" && q.Status == UploadStatus.Uploaded, cancellationToken);

            if (!isVideoUploaded || !isPdfUploaded)
            {
                // Safety guard: do not delete local files if Google Drive upload is not verified
                continue;
            }

            // Delete video file if exists
            if (!string.IsNullOrEmpty(lecture.VideoFileLocalPath) && File.Exists(lecture.VideoFileLocalPath))
            {
                try
                {
                    var fileInfo = new FileInfo(lecture.VideoFileLocalPath);
                    var size = fileInfo.Length;
                    File.Delete(lecture.VideoFileLocalPath);
                    bytesReclaimed += size;
                    _logger.LogInformation($"Storage Cleaner deleted uploaded video: {Path.GetFileName(lecture.VideoFileLocalPath)} (Freed {size / (1024 * 1024):N1} MB)");
                    lecture.VideoFileLocalPath = "[Archived to Drive]";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not delete file {lecture.VideoFileLocalPath}: {ex.Message}");
                }
            }

            // Delete PDF file if exists
            if (!string.IsNullOrEmpty(lecture.PdfFileLocalPath) && File.Exists(lecture.PdfFileLocalPath))
            {
                try
                {
                    var fileInfo = new FileInfo(lecture.PdfFileLocalPath);
                    var size = fileInfo.Length;
                    File.Delete(lecture.PdfFileLocalPath);
                    bytesReclaimed += size;
                    _logger.LogInformation($"Storage Cleaner deleted uploaded notes: {Path.GetFileName(lecture.PdfFileLocalPath)} (Freed {size / 1024:N1} KB)");
                    lecture.PdfFileLocalPath = "[Archived to Drive]";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not delete file {lecture.PdfFileLocalPath}: {ex.Message}");
                }
            }

            lecture.Status = LectureStatus.Archived;
            lecture.UpdatedAt = DateTime.UtcNow;
        }

        if (bytesReclaimed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation($"Storage Cleanup completed. Total space reclaimed: {bytesReclaimed / (1024 * 1024):N1} MB");
        }

        return bytesReclaimed;
    }
}
