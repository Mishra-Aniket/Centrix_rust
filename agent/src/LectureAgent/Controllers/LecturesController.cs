namespace LectureAgent.Presentation.Controllers;

using LectureAgent.Application.Services;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using LectureAgent.Infrastructure.QualityCheck;
using LectureAgent.Infrastructure.YouTube;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.IO;

/// <summary>
/// REST API controller for lecture sessions.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LecturesController : ControllerBase
{
    private readonly LectureSessionService _lectureService;
    private readonly MatchingService _matchingService;
    private readonly UploadQueueService _queueService;
    private readonly ILectureRepository _repository;
    private readonly IConfiguration _configuration;
    private readonly IFileWatcher _fileWatcher;
    private readonly IAuditLogger _auditLogger;
    private readonly IQcChecker _qcChecker;
    private readonly YouTubePublishQueue _youTubeQueue;
    private readonly ILogger<LecturesController> _logger;

    public LecturesController(
        LectureSessionService lectureService,
        MatchingService matchingService,
        UploadQueueService queueService,
        ILectureRepository repository,
        IConfiguration configuration,
        IFileWatcher fileWatcher,
        IAuditLogger auditLogger,
        IQcChecker qcChecker,
        YouTubePublishQueue youTubeQueue,
        ILogger<LecturesController> logger)
    {
        _lectureService = lectureService;
        _matchingService = matchingService;
        _queueService = queueService;
        _repository = repository;
        _configuration = configuration;
        _fileWatcher = fileWatcher;
        _auditLogger = auditLogger;
        _qcChecker = qcChecker;
        _youTubeQueue = youTubeQueue;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new lecture session.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<LectureSessionDto>> CreateLecture(
        [FromBody] CreateLectureRequest request)
    {
        _logger.LogInformation($"Creating lecture: {request.VideoFilePath}");

        try
        {
            var session = await _lectureService.CreateSessionAsync(
                request.OrganizationId,
                request.CenterId,
                request.RoomId,
                request.DeviceId,
                request.VideoFilePath,
                request.VideoFileSize,
                request.DetectedStartTime,
                request.DetectedEndTime);

            return CreatedAtAction(nameof(GetLecture), 
                new { lectureSessionId = session.LectureSessionId },
                MapToDto(session));
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating lecture: {ex.Message}");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Manually upload and ingest a video or PDF notes file from the web app.
    /// Supports large lecture recordings up to 10 GB.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(10L * 1024 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024)]
    public async Task<ActionResult> UploadLecture(
        IFormFile file,
        [FromForm] string? roomId,
        [FromForm] string? batchId,
        [FromForm] string? subjectId,
        [FromForm] string? teacherId,
        [FromForm] string? driveFolderPath)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file was uploaded or file is empty" });

        var organizationId = _configuration["Agent:OrganizationId"] ?? "PCMC_VIDYAPEETH";
        var centerId = _configuration["Agent:CenterId"] ?? "Pune - PCMC Vidyapeeth";
        var effectiveRoomId = !string.IsNullOrWhiteSpace(roomId) ? roomId.Trim() : (_configuration["Agent:RoomId"] ?? "603");
        var deviceId = _configuration["Agent:DeviceId"] ?? Environment.MachineName;

        // Determine destination folder (prefer active monitored folder or fallback to uploads)
        var monitorFolder = _fileWatcher.MonitoredFolderPath ?? _configuration["FileWatcher:MonitorFolder"];
        if (string.IsNullOrWhiteSpace(monitorFolder) || !Directory.Exists(monitorFolder))
        {
            monitorFolder = Path.Combine(AppContext.BaseDirectory, "uploads");
            Directory.CreateDirectory(monitorFolder);
        }

        // Clean original filename
        var safeFileName = Path.GetFileName(file.FileName);
        var targetFilePath = Path.Combine(monitorFolder, safeFileName);

        // If file exists with same name, append timestamp to prevent overwriting
        if (System.IO.File.Exists(targetFilePath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(safeFileName);
            var ext = Path.GetExtension(safeFileName);
            targetFilePath = Path.Combine(monitorFolder, $"{nameWithoutExt}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{ext}");
        }

        _logger.LogInformation("Saving manual upload: {FileName} ({Size} bytes) -> {Path}", safeFileName, file.Length, targetFilePath);

        await using (var stream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(stream);
        }

        var extLower = Path.GetExtension(targetFilePath).ToLowerInvariant();
        var isPdf = extLower == ".pdf";

        var detectedEnd = DateTime.UtcNow;
        var detectedStart = detectedEnd.AddMinutes(-90);

        var session = await _lectureService.CreateSessionAsync(
            organizationId: organizationId,
            centerId: centerId,
            roomId: effectiveRoomId,
            deviceId: deviceId,
            videoFilePath: targetFilePath,
            videoFileSize: file.Length,
            detectedStart: detectedStart,
            detectedEnd: detectedEnd);

        _logger.LogInformation("Manual upload created lecture session: {LectureSessionId}", session.LectureSessionId);

        // Manual batch assignment specified by user
        if (!string.IsNullOrWhiteSpace(batchId))
        {
            var sub = !string.IsNullOrWhiteSpace(subjectId) ? subjectId.Trim() : batchId.Trim();
            var teach = teacherId?.Trim() ?? "";
            session = await _lectureService.ConfirmAssignmentAsync(
                session.LectureSessionId,
                batchId.Trim(),
                sub,
                teach,
                "Manual Web Upload");

            if (!string.IsNullOrWhiteSpace(driveFolderPath))
            {
                var trimmed = driveFolderPath.Trim();
                if (!trimmed.Contains('/') && !trimmed.Contains('\\') && !string.IsNullOrWhiteSpace(sub))
                {
                    trimmed = $"{trimmed}/{sub}";
                }
                session.DriveFolderPath = trimmed;
                await _repository.UpdateAsync(session);
            }

            var fileType = isPdf ? "PDF" : "VIDEO";
            await _queueService.EnqueueFileAsync(
                lectureSessionId: session.LectureSessionId,
                fileType: fileType,
                localFilePath: targetFilePath,
                fileSizeBytes: file.Length,
                fileHash: session.VideoFileHash,
                driveFolderPath: session.DriveFolderPath ?? driveFolderPath?.Trim(),
                allowDuplicate: true);

            return Ok(new
            {
                lecture = MapToDto(session),
                message = $"Uploaded and assigned to {batchId.Trim()} ({sub}). Upload queued to Google Drive.",
                autoAssigned = false
            });
        }

        // Automatic matching engine analysis
        var matchResult = await _matchingService.AnalyzeAndAssignAsync(session);
        _logger.LogInformation("Upload auto-match result: {Decision} (confidence: {Score}%)",
            matchResult.Decision, matchResult.ConfidenceScore);

        var refreshed = await _repository.GetByIdAsync(session.LectureSessionId) ?? session;

        if (matchResult.Decision == MatchingDecision.AutoAssigned || isPdf)
        {
            var fileType = isPdf ? "PDF" : "VIDEO";
            await _queueService.EnqueueFileAsync(
                lectureSessionId: refreshed.LectureSessionId,
                fileType: fileType,
                localFilePath: targetFilePath,
                fileSizeBytes: file.Length,
                fileHash: refreshed.VideoFileHash,
                driveFolderPath: refreshed.DriveFolderPath);
        }

        var statusMessage = matchResult.Decision == MatchingDecision.AutoAssigned
            ? $"Matched to {matchResult.MatchedSlot?.BatchId} ({matchResult.MatchedSlot?.SubjectId}) and queued for upload to Drive!"
            : "Uploaded successfully. Placed in Review Queue for manual batch confirmation.";

        return Ok(new
        {
            lecture = MapToDto(refreshed),
            decision = matchResult.Decision.ToString(),
            confidence = matchResult.ConfidenceScore,
            matchedBatch = matchResult.MatchedSlot?.BatchId,
            message = statusMessage
        });
    }

    /// <summary>
    /// Gets a lecture by ID.
    /// </summary>
    [HttpGet("{lectureSessionId}")]
    public async Task<ActionResult<LectureSessionDto>> GetLecture(string lectureSessionId)
    {
        var lecture = await _repository.GetByIdAsync(lectureSessionId);
        if (lecture == null)
            return NotFound();

        return Ok(MapToDto(lecture));
    }

    /// <summary>
    /// Lists lectures with optional filtering.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<object>> ListLectures(
        [FromQuery] string? centerId,
        [FromQuery] string? status,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        LectureStatus? statusEnum = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<LectureStatus>(status, true, out var parsedStatus))
        {
            statusEnum = parsedStatus;
        }

        var (items, totalCount) = await _repository.GetPagedLecturesAsync(centerId, statusEnum, offset, limit);
        var result = items.Select(MapToDto).ToList();

        return Ok(new { total = totalCount, count = result.Count, items = result });
    }

    /// <summary>
    /// Confirms a lecture assignment.
    /// </summary>
    [HttpPut("{lectureSessionId}/confirm")]
    public async Task<ActionResult<LectureSessionDto>> ConfirmLecture(
        string lectureSessionId,
        [FromBody] ConfirmLectureRequest request)
    {
        try
        {
            var session = await _lectureService.ConfirmAssignmentAsync(
                lectureSessionId,
                request.BatchId,
                request.SubjectId,
                request.TeacherId,
                request.ReviewedBy);

            if (!string.IsNullOrWhiteSpace(request.DriveFolderPath))
            {
                var folder = request.DriveFolderPath.Trim();
                if (!string.IsNullOrWhiteSpace(request.SubjectId) && !folder.Contains('/') && !folder.Contains('\\'))
                {
                    folder = $"{folder}/{request.SubjectId.Trim()}";
                }
                session.DriveFolderPath = folder;
                await _repository.UpdateAsync(session);
            }

            // Sync any existing queue entries
            await _queueService.SyncLectureDriveFolderAsync(session);

            // Ensure video and/or PDF file is enqueued for upload to Google Drive
            var existingQueue = await _queueService.GetByLectureSessionIdAsync(session.LectureSessionId);
            var hasVideoInQueue = existingQueue.Any(q => q.FileType.Equals("VIDEO", StringComparison.OrdinalIgnoreCase));
            var hasPdfInQueue = existingQueue.Any(q => q.FileType.Equals("PDF", StringComparison.OrdinalIgnoreCase));

            if (!hasVideoInQueue && !string.IsNullOrWhiteSpace(session.VideoFileLocalPath) && System.IO.File.Exists(session.VideoFileLocalPath))
            {
                var vInfo = new FileInfo(session.VideoFileLocalPath);
                await _queueService.EnqueueFileAsync(
                    session.LectureSessionId,
                    "VIDEO",
                    session.VideoFileLocalPath,
                    vInfo.Length,
                    session.VideoFileHash,
                    session.DriveFolderPath,
                    allowDuplicate: true);
            }

            if (!hasPdfInQueue && !string.IsNullOrWhiteSpace(session.PdfFileLocalPath) && System.IO.File.Exists(session.PdfFileLocalPath))
            {
                var pInfo = new FileInfo(session.PdfFileLocalPath);
                await _queueService.EnqueueFileAsync(
                    session.LectureSessionId,
                    "PDF",
                    session.PdfFileLocalPath,
                    pInfo.Length,
                    session.PdfFileHash,
                    session.DriveFolderPath,
                    allowDuplicate: true);
            }

            return Ok(MapToDto(session));
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error confirming lecture: {ex.Message}");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Cancels and rejects a lecture session and any associated upload queue entries.
    /// </summary>
    [HttpPost("{lectureSessionId}/cancel")]
    public async Task<ActionResult<LectureSessionDto>> CancelLecture(string lectureSessionId)
    {
        var session = await _repository.GetByIdAsync(lectureSessionId);
        if (session == null)
            return NotFound();

        session.Status = LectureStatus.Cancelled;
        session.ReviewStatus = ReviewStatus.Rejected;
        session.UpdatedAt = DateTime.UtcNow;
        session.LastStatusChange = DateTime.UtcNow;
        await _repository.UpdateAsync(session);
        await _repository.SaveChangesAsync();

        // Cancel any pending queue entries
        var queueEntries = await _queueService.GetByLectureSessionIdAsync(lectureSessionId);
        foreach (var entry in queueEntries)
        {
            if (entry.Status != UploadStatus.Uploaded)
            {
                await _queueService.CancelAsync(entry.QueueEntryId);
            }
        }

        _logger.LogInformation("Lecture {LectureSessionId} cancelled and rejected", lectureSessionId);

        await _auditLogger.LogAsync("LECTURE_SESSION", lectureSessionId, "CANCELLED", null,
            null, new { session.Status, session.ReviewStatus },
            "Lecture manually cancelled by dashboard user");

        return Ok(MapToDto(session));
    }

    /// <summary>
    /// Enqueues the lecture's local file for upload to Google Drive immediately.
    /// </summary>
    [HttpPost("{lectureSessionId}/enqueue-upload")]
    public async Task<ActionResult<LectureSessionDto>> EnqueueUpload(string lectureSessionId)
    {
        var session = await _repository.GetByIdAsync(lectureSessionId);
        if (session == null)
            return NotFound();

        var filePath = session.VideoFileLocalPath ?? session.PdfFileLocalPath;
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            return BadRequest(new { error = $"File not found on local disk: {filePath}" });

        var isPdf = Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        var fileInfo = new FileInfo(filePath);
        await _queueService.EnqueueFileAsync(
            session.LectureSessionId,
            isPdf ? "PDF" : "VIDEO",
            filePath,
            fileInfo.Length,
            session.VideoFileHash ?? session.PdfFileHash,
            session.DriveFolderPath ?? session.BatchId,
            allowDuplicate: true);

        return Ok(MapToDto(session));
    }

    /// <summary>
    /// Uploads a duplicate-flagged lecture anyway (reviewer override).
    /// </summary>
    [HttpPost("{lectureSessionId}/force-enqueue")]
    public async Task<ActionResult<LectureSessionDto>> ForceEnqueue(string lectureSessionId)
    {
        var lecture = await _repository.GetByIdAsync(lectureSessionId);
        if (lecture == null)
            return NotFound();

        if (lecture.Status != LectureStatus.Duplicate)
            return Conflict(new { error = "Only duplicate-flagged lectures can be force-uploaded" });

        if (string.IsNullOrWhiteSpace(lecture.VideoFileLocalPath))
            return Conflict(new { error = "Lecture has no video file path" });

        var queueEntry = await _queueService.EnqueueFileAsync(
            lectureSessionId: lecture.LectureSessionId,
            fileType: "VIDEO",
            localFilePath: lecture.VideoFileLocalPath,
            fileSizeBytes: lecture.VideoFileSizeBytes ?? 0,
            fileHash: lecture.VideoFileHash,
            driveFolderPath: lecture.DriveFolderPath,
            allowDuplicate: true);

        if (queueEntry == null)
            return Conflict(new { error = "An identical recording is already uploaded or uploading" });

        var updated = await _lectureService.UpdateStatusAsync(
            lectureSessionId,
            LectureStatus.Confirmed,
            "Reviewer chose to upload despite the duplicate flag");

        await _auditLogger.LogAsync("LECTURE_SESSION", lectureSessionId, "FORCE_ENQUEUED", null,
            new { OriginalStatus = LectureStatus.Duplicate },
            new { updated.Status, QueueEntryId = queueEntry.QueueEntryId },
            "Duplicate lecture force-enqueued for upload by dashboard user");

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Re-runs the matching engine for an existing lecture (e.g. after a timetable fix).
    /// </summary>
    [HttpPost("{lectureSessionId}/rematch")]
    public async Task<ActionResult<ActionResponse<LectureSessionDto>>> RematchLecture(string lectureSessionId)
    {
        var lecture = await _repository.GetByIdAsync(lectureSessionId);
        if (lecture == null)
        {
            return NotFound(new ActionResponse<LectureSessionDto>
            {
                Success = false,
                Message = $"Lecture {lectureSessionId} not found"
            });
        }

        var result = await _matchingService.AnalyzeAndAssignAsync(lecture);
        _logger.LogInformation("Rematch for {Id}: {Decision} (confidence: {Score}%)",
            lectureSessionId, result.Decision, result.ConfidenceScore);

        var updated = await _repository.GetByIdAsync(lectureSessionId) ?? lecture;
        var message = result.Decision switch
        {
            MatchingDecision.AutoAssigned =>
                $"Auto-matched to {result.MatchedSlot?.BatchId}/{result.MatchedSlot?.SubjectId} ({result.ConfidenceScore}% confidence)",
            MatchingDecision.ReviewRequired =>
                $"Suggested match: {result.MatchedSlot?.BatchId}/{result.MatchedSlot?.SubjectId} ({result.ConfidenceScore}% confidence) — needs review",
            _ => $"No match found: {result.ReasoningText}"
        };

        return Ok(new ActionResponse<LectureSessionDto>
        {
            Success = result.Decision != MatchingDecision.NoMatch,
            Message = message,
            Data = MapToDto(updated)
        });
    }

    /// <summary>
    /// Returns a center-scoped view of lecture pipeline health.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<LectureSummaryDto>> GetSummary([FromQuery, Required] string centerId)
    {
        var summary = await _repository.GetSummaryAsync(centerId);
        return Ok(new LectureSummaryDto
        {
            Total = summary.Total,
            Uploaded = summary.Uploaded,
            Matched = summary.Matched,
            Unmatched = summary.Unmatched,
            FailedUpload = summary.FailedUpload,
            PendingReview = summary.PendingReview,
            UploadedPercentage = summary.Total > 0 ? Math.Round(100.0 * summary.Uploaded / summary.Total, 1) : 0,
            MatchedPercentage = summary.Total > 0 ? Math.Round(100.0 * summary.Matched / summary.Total, 1) : 0
        });
    }

    /// <summary>
    /// Resets every failed upload for a lecture so the background processor can retry it.
    /// </summary>
    [HttpPost("{lectureSessionId}/retry-upload")]
    public async Task<ActionResult<ActionResponse<LectureSessionDto>>> RetryUpload(string lectureSessionId)
    {
        var lecture = await _repository.GetByIdAsync(lectureSessionId);
        if (lecture == null)
        {
            return NotFound(new ActionResponse<LectureSessionDto>
            {
                Success = false,
                Message = $"Lecture {lectureSessionId} not found"
            });
        }

        var queueEntries = await _queueService.GetByLectureSessionIdAsync(lectureSessionId);
        var failedEntries = queueEntries.Where(e =>
            e.Status == UploadStatus.Failed || e.Status == UploadStatus.FailedPermanently).ToList();

        if (failedEntries.Count == 0)
        {
            return Conflict(new ActionResponse<LectureSessionDto>
            {
                Success = false,
                Message = "No failed uploads to retry for this lecture"
            });
        }

        foreach (var entry in failedEntries)
            await _queueService.RetryAsync(entry.QueueEntryId);

        if (lecture.Status == LectureStatus.UploadFailed)
        {
            lecture.Status = LectureStatus.Confirmed;
            lecture.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(lecture);
            await _repository.SaveChangesAsync();
        }

        await _auditLogger.LogAsync("LECTURE_SESSION", lectureSessionId, "RETRY_UPLOAD", null,
            null, new { RetriedCount = failedEntries.Count },
            $"Retried {failedEntries.Count} failed upload(s) for this lecture");

        var refreshed = await _repository.GetByIdAsync(lectureSessionId) ?? lecture;
        return Ok(new ActionResponse<LectureSessionDto>
        {
            Success = true,
            Message = $"Retried {failedEntries.Count} failed upload(s)",
            Data = MapToDto(refreshed)
        });
    }

    /// <summary>
    /// Runs local quality checks against the lecture notes PDF and saves the latest report.
    /// </summary>
    [HttpGet("{lectureSessionId}/qc")]
    public async Task<ActionResult<ActionResponse<QcReport>>> GetQualityCheck(string lectureSessionId)
    {
        var lecture = await _repository.GetByIdAsync(lectureSessionId);
        if (lecture == null)
        {
            return NotFound(new ActionResponse<QcReport>
            {
                Success = false,
                Message = $"Lecture {lectureSessionId} not found"
            });
        }

        var report = await _qcChecker.CheckAsync(lecture, HttpContext.RequestAborted);
        lecture.QcResults = System.Text.Json.JsonSerializer.Serialize(report);
        lecture.QcStatus = report.Status;
        lecture.QcPassedAt = report.Status == QcStatus.Passed ? report.CheckedAt : null;
        lecture.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(lecture);
        await _repository.SaveChangesAsync();
        await _auditLogger.LogAsync("LECTURE_SESSION", lectureSessionId, "QC_CHECKED", null,
            null, new { report.Status, CheckCount = report.Checks.Count }, "Local quality-control report refreshed");

        return Ok(new ActionResponse<QcReport>
        {
            Success = report.Status == QcStatus.Passed,
            Message = report.Status == QcStatus.Passed ? "QC passed" : "QC found items requiring review",
            Data = report
        });
    }

    /// <summary>
    /// Queues an unlisted YouTube publication for a locally available lecture video.
    /// </summary>
    [HttpPost("{lectureSessionId}/publish")]
    public async Task<ActionResult<ActionResponse<LectureSessionDto>>> PublishToYouTube(string lectureSessionId)
    {
        var lecture = await _repository.GetByIdAsync(lectureSessionId);
        if (lecture == null)
        {
            return NotFound(new ActionResponse<LectureSessionDto>
            {
                Success = false,
                Message = $"Lecture {lectureSessionId} not found"
            });
        }
        if (!_configuration.GetValue("YouTube:Enabled", false))
        {
            return Conflict(new ActionResponse<LectureSessionDto>
            {
                Success = false,
                Message = "YouTube publishing is disabled. Configure YouTube OAuth and set YouTube:Enabled to true first."
            });
        }
        if (string.IsNullOrWhiteSpace(lecture.VideoFileLocalPath) || !System.IO.File.Exists(lecture.VideoFileLocalPath))
        {
            return Conflict(new ActionResponse<LectureSessionDto>
            {
                Success = false,
                Message = "A local lecture video is required before publishing to YouTube."
            });
        }
        if (lecture.YouTubePublishStatus is YouTubePublishStatus.PublishQueued or YouTubePublishStatus.Publishing)
        {
            return Conflict(new ActionResponse<LectureSessionDto>
            {
                Success = false,
                Message = "This lecture is already queued for YouTube publishing."
            });
        }

        lecture.YouTubePublishStatus = YouTubePublishStatus.PublishQueued;
        lecture.YouTubeFailureReason = null;
        lecture.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(lecture);
        await _repository.SaveChangesAsync();
        _youTubeQueue.TryEnqueue(new YouTubePublishJob(lectureSessionId, YouTubePublishJobKind.Publish));
        await _auditLogger.LogAsync("LECTURE_SESSION", lectureSessionId, "YOUTUBE_PUBLISH_QUEUED", null,
            null, null, "Unlisted YouTube publication queued");

        return Accepted(new ActionResponse<LectureSessionDto>
        {
            Success = true,
            Message = "YouTube publication queued as unlisted.",
            Data = MapToDto(lecture)
        });
    }

    /// <summary>
    /// Queues an unpublish operation, which changes the existing YouTube video's privacy to private.
    /// </summary>
    [HttpPost("{lectureSessionId}/unpublish")]
    public async Task<ActionResult<ActionResponse<LectureSessionDto>>> UnpublishFromYouTube(string lectureSessionId)
    {
        var lecture = await _repository.GetByIdAsync(lectureSessionId);
        if (lecture == null)
        {
            return NotFound(new ActionResponse<LectureSessionDto>
            {
                Success = false,
                Message = $"Lecture {lectureSessionId} not found"
            });
        }
        if (!_configuration.GetValue("YouTube:Enabled", false))
        {
            return Conflict(new ActionResponse<LectureSessionDto>
            {
                Success = false,
                Message = "YouTube publishing is disabled."
            });
        }
        if (string.IsNullOrWhiteSpace(lecture.YouTubeId))
        {
            return Conflict(new ActionResponse<LectureSessionDto>
            {
                Success = false,
                Message = "This lecture has no published YouTube video."
            });
        }
        if (lecture.YouTubePublishStatus is YouTubePublishStatus.UnpublishQueued or YouTubePublishStatus.Unpublishing)
        {
            return Conflict(new ActionResponse<LectureSessionDto>
            {
                Success = false,
                Message = "This lecture is already queued to be made private."
            });
        }

        lecture.YouTubePublishStatus = YouTubePublishStatus.UnpublishQueued;
        lecture.YouTubeFailureReason = null;
        lecture.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(lecture);
        await _repository.SaveChangesAsync();
        _youTubeQueue.TryEnqueue(new YouTubePublishJob(lectureSessionId, YouTubePublishJobKind.Unpublish));
        await _auditLogger.LogAsync("LECTURE_SESSION", lectureSessionId, "YOUTUBE_UNPUBLISH_QUEUED", null,
            null, new { lecture.YouTubeId }, "YouTube video will be made private");

        return Accepted(new ActionResponse<LectureSessionDto>
        {
            Success = true,
            Message = "YouTube video will be made private.",
            Data = MapToDto(lecture)
        });
    }

    /// <summary>
    /// Gets pending reviews for a center.
    /// </summary>
    [HttpGet("review-queue")]
    public async Task<ActionResult<List<LectureSessionDto>>> GetReviewQueue(
        [FromQuery, Required] string centerId)
    {
        var reviews = await _lectureService.GetPendingReviewAsync(centerId);
        return Ok(reviews.Select(MapToDto).ToList());
    }

    private static LectureSessionDto MapToDto(LectureSession lecture)
    {
        return new LectureSessionDto
        {
            LectureSessionId = lecture.LectureSessionId,
            OrganizationId = lecture.OrganizationId,
            CenterId = lecture.CenterId,
            RoomId = lecture.RoomId,
            DeviceId = lecture.DeviceId,
            DetectedStartTime = lecture.DetectedStartTime,
            DetectedEndTime = lecture.DetectedEndTime,
            DetectedDurationSeconds = lecture.DetectedDurationSeconds,
            BatchId = lecture.BatchId,
            SubjectId = lecture.SubjectId,
            TeacherId = lecture.TeacherId,
            ConfidenceScore = lecture.ConfidenceScore,
            Status = lecture.Status.ToString(),
            ReviewStatus = lecture.ReviewStatus.ToString(),
            MatchStatus = lecture.MatchStatus.ToString(),
            FailureCode = lecture.FailureCode.ToString(),
            FailureReason = lecture.FailureReason,
            MatchedAt = lecture.MatchedAt,
            MatchAttempts = lecture.MatchAttempts,
            YouTubeId = lecture.YouTubeId,
            YouTubePublishStatus = lecture.YouTubePublishStatus.ToString(),
            YouTubeThumbnailUrl = lecture.YouTubeThumbnailUrl,
            YouTubeFailureReason = lecture.YouTubeFailureReason,
            YouTubePublishedAt = lecture.YouTubePublishedAt,
            QcStatus = lecture.QcStatus.ToString(),
            QcResults = lecture.QcResults,
            QcPassedAt = lecture.QcPassedAt,
            CreatedAt = lecture.CreatedAt,
            UpdatedAt = lecture.UpdatedAt,
            VideoFilePath = lecture.VideoFileLocalPath,
            VideoFileSize = lecture.VideoFileSizeBytes,
            PdfFilePath = lecture.PdfFileLocalPath,
            PdfFileSize = lecture.PdfFileSizeBytes,
            DriveFolderPath = lecture.DriveFolderPath,
            DriveVideoFileId = lecture.DriveVideoFileId,
            DrivePdfFileId = lecture.DrivePdfFileId
        };
    }

    /// <summary>
    /// Streams or previews the lecture video or PDF file directly.
    /// Supports HTTP 206 Partial Content byte-range requests for smooth seeking in video players.
    /// </summary>
    [HttpGet("{lectureSessionId}/media/{type}")]
    public async Task<IActionResult> GetLectureMedia(string lectureSessionId, string type)
    {
        var lecture = await _repository.GetByIdAsync(lectureSessionId);
        if (lecture == null)
            return NotFound(new { message = "Lecture session not found." });

        var filePath = type.Equals("pdf", StringComparison.OrdinalIgnoreCase)
            ? lecture.PdfFileLocalPath
            : lecture.VideoFileLocalPath;

        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            return NotFound(new { message = "Media file not found locally on this machine." });

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream"
        };

        return PhysicalFile(Path.GetFullPath(filePath), contentType, enableRangeProcessing: true);
    }
}

/// <summary>
/// DTO for lecture session.
/// </summary>
public class LectureSessionDto
{
    public string LectureSessionId { get; set; } = null!;
    public string OrganizationId { get; set; } = null!;
    public string CenterId { get; set; } = null!;
    public string RoomId { get; set; } = null!;
    public string DeviceId { get; set; } = null!;
    public DateTime DetectedStartTime { get; set; }
    public DateTime DetectedEndTime { get; set; }
    public int DetectedDurationSeconds { get; set; }
    public string? BatchId { get; set; }
    public string? SubjectId { get; set; }
    public string? TeacherId { get; set; }
    public int ConfidenceScore { get; set; }
    public string Status { get; set; } = null!;
    public string ReviewStatus { get; set; } = null!;
    public string? MatchStatus { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? MatchedAt { get; set; }
    public int MatchAttempts { get; set; }
    public string? YouTubeId { get; set; }
    public string? YouTubePublishStatus { get; set; }
    public string? YouTubeThumbnailUrl { get; set; }
    public string? YouTubeFailureReason { get; set; }
    public DateTime? YouTubePublishedAt { get; set; }
    public string? QcStatus { get; set; }
    public string? QcResults { get; set; }
    public DateTime? QcPassedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? VideoFilePath { get; set; }
    public long? VideoFileSize { get; set; }
    public string? PdfFilePath { get; set; }
    public long? PdfFileSize { get; set; }
    public string? DriveFolderPath { get; set; }
    public string? DriveVideoFileId { get; set; }
    public string? DrivePdfFileId { get; set; }
}

/// <summary>
/// Standard response envelope for action endpoints.
/// </summary>
public class ActionResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = null!;
    public T? Data { get; set; }
}

/// <summary>
/// Center-scoped lecture pipeline totals and rates.
/// </summary>
public class LectureSummaryDto
{
    public int Total { get; set; }
    public int Uploaded { get; set; }
    public int Matched { get; set; }
    public int Unmatched { get; set; }
    public int FailedUpload { get; set; }
    public int PendingReview { get; set; }
    public double UploadedPercentage { get; set; }
    public double MatchedPercentage { get; set; }
}

/// <summary>
/// Request to create a lecture.
/// </summary>
public class CreateLectureRequest
{
    [Required]
    public string OrganizationId { get; set; } = null!;

    [Required]
    public string CenterId { get; set; } = null!;

    [Required]
    public string RoomId { get; set; } = null!;

    [Required]
    public string DeviceId { get; set; } = null!;

    [Required]
    public string VideoFilePath { get; set; } = null!;

    [Required, Range(1, long.MaxValue)]
    public long VideoFileSize { get; set; }

    [Required]
    public DateTime DetectedStartTime { get; set; }

    [Required]
    public DateTime DetectedEndTime { get; set; }
}

/// <summary>
/// Request to confirm a lecture.
/// </summary>
public class ConfirmLectureRequest
{
    [Required]
    public string BatchId { get; set; } = null!;

    [Required]
    public string SubjectId { get; set; } = null!;

    public string? TeacherId { get; set; }

    public string? ReviewedBy { get; set; }

    public string? DriveFolderPath { get; set; }
}
