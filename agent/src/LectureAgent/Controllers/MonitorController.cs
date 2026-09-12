using LectureAgent.Application.Services;
using LectureAgent.Domain.Enums;
using LectureAgent.Infrastructure.Database;
using LectureAgent.Infrastructure.Cloud;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace LectureAgent.Presentation.Controllers;

[ApiController]
[Route("api/monitor")]
public sealed class MonitorController : ControllerBase
{
    private readonly LectureContext _dbContext;
    private readonly UploadProgressStore _progressStore;

    public MonitorController(LectureContext dbContext, UploadProgressStore progressStore)
    {
        _dbContext = dbContext;
        _progressStore = progressStore;
    }

    [HttpGet("snapshot")]
    public async Task<ActionResult<MonitorSnapshotDto>> Snapshot()
    {
        var queues = await _dbContext.UploadQueues
            .AsNoTracking()
            .OrderByDescending(queue => queue.UpdatedAt)
            .Take(25)
            .Select(queue => new UploadMonitorDto
            {
                QueueEntryId = queue.QueueEntryId,
                FileType = queue.FileType,
                FileName = queue.DriveFileName ?? Path.GetFileName(queue.LocalFilePath),
                LocalFilePath = queue.LocalFilePath,
                FileSizeBytes = queue.FileSizeBytes,
                BytesUploaded = queue.BytesUploaded,
                Status = queue.Status.ToString(),
                ProgressPercentage = queue.FileSizeBytes == 0
                    ? 0
                    : (int)(queue.BytesUploaded * 100d / queue.FileSizeBytes),
                RetryCount = queue.RetryCount,
                DriveFileId = queue.DriveFileId,
                LastError = queue.LastError,
                UpdatedAt = queue.UpdatedAt
            })
            .ToListAsync();

        var lectures = await _dbContext.LectureSessions
            .AsNoTracking()
            .OrderByDescending(lecture => lecture.UpdatedAt)
            .Take(10)
            .Select(lecture => new LectureMonitorDto
            {
                LectureSessionId = lecture.LectureSessionId,
                FileName = lecture.VideoFileLocalPath ?? lecture.PdfFileLocalPath ?? "Unknown file",
                Status = lecture.Status.ToString(),
                ConfidenceScore = lecture.ConfidenceScore,
                CenterId = lecture.CenterId,
                RoomId = lecture.RoomId,
                UpdatedAt = lecture.UpdatedAt
            })
            .ToListAsync();

        foreach (var queue in queues)
        {
            if (_progressStore.TryGet(queue.QueueEntryId, out var bytesUploaded))
            {
                queue.BytesUploaded = bytesUploaded;
                queue.ProgressPercentage = queue.FileSizeBytes == 0
                    ? 0
                    : (int)(bytesUploaded * 100d / queue.FileSizeBytes);
            }
        }

        return Ok(new MonitorSnapshotDto
        {
            GeneratedAt = DateTime.UtcNow,
            Queue = queues,
            Lectures = lectures,
            Summary = new MonitorSummaryDto
            {
                Pending = await _dbContext.UploadQueues.CountAsync(queue => queue.Status == UploadStatus.Pending),
                Uploading = await _dbContext.UploadQueues.CountAsync(queue => queue.Status == UploadStatus.Uploading),
                Uploaded = await _dbContext.UploadQueues.CountAsync(queue => queue.Status == UploadStatus.Uploaded),
                Failed = await _dbContext.UploadQueues.CountAsync(queue => queue.Status == UploadStatus.Failed || queue.Status == UploadStatus.FailedPermanently),
                TotalLectures = await _dbContext.LectureSessions.CountAsync()
            }
        });
    }

    /// <summary>
    /// Timetable slots for the day that finished without any recording detected.
    /// Cancelled slots (active overrides) are excluded.
    /// </summary>
    [HttpGet("missing")]
    public async Task<ActionResult<List<MissingSlotInfo>>> Missing(
        [FromQuery, Required] string centerId,
        [FromQuery, Required] string roomId,
        [FromQuery] DateTime? date,
        [FromQuery] int graceMinutes = 30)
    {
        var day = (date ?? DateTime.Now).Date;
        var nextDay = day.AddDays(1);

        var entries = await _dbContext.TimetableEntries
            .AsNoTracking()
            .Where(entry => entry.CenterId == centerId
                && entry.RoomId == roomId
                && entry.ScheduledDate >= day
                && entry.ScheduledDate < nextDay)
            .ToListAsync();

        // SQLite cannot ORDER BY a TimeSpan column, so sort after materializing.
        entries = entries.OrderBy(entry => entry.SlotStartTime).ToList();

        var overrides = await _dbContext.TimetableOverrides
            .AsNoTracking()
            .Where(item => item.CenterId == centerId
                && item.EffectiveDate >= day
                && item.EffectiveDate < nextDay
                && item.IsActive)
            .ToListAsync();

        // DetectedStartTime is written as local wall-clock by file detection,
        // so raw comparison against the local slot window is intentional.
        var lectures = await _dbContext.LectureSessions
            .AsNoTracking()
            .Where(lecture => lecture.CenterId == centerId
                && lecture.RoomId == roomId
                && lecture.DetectedStartTime >= day
                && lecture.DetectedStartTime < nextDay)
            .ToListAsync();

        var missing = MissingLectureDetector.FindMissing(entries, overrides, lectures, DateTime.Now, graceMinutes);
        return Ok(missing);
    }
}

public sealed class MonitorSnapshotDto
{
    public DateTime GeneratedAt { get; set; }
    public MonitorSummaryDto Summary { get; set; } = new();
    public List<UploadMonitorDto> Queue { get; set; } = new();
    public List<LectureMonitorDto> Lectures { get; set; } = new();
}

public sealed class MonitorSummaryDto
{
    public int Pending { get; set; }
    public int Uploading { get; set; }
    public int Uploaded { get; set; }
    public int Failed { get; set; }
    public int TotalLectures { get; set; }
}

public sealed class UploadMonitorDto
{
    public string QueueEntryId { get; set; } = null!;
    public string FileType { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string LocalFilePath { get; set; } = null!;
    public long FileSizeBytes { get; set; }
    public long BytesUploaded { get; set; }
    public string Status { get; set; } = null!;
    public int ProgressPercentage { get; set; }
    public int RetryCount { get; set; }
    public string? DriveFileId { get; set; }
    public string? LastError { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class LectureMonitorDto
{
    public string LectureSessionId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int ConfidenceScore { get; set; }
    public string CenterId { get; set; } = null!;
    public string RoomId { get; set; } = null!;
    public DateTime UpdatedAt { get; set; }
}
