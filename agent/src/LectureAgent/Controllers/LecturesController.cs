namespace LectureAgent.Presentation.Controllers;

using LectureAgent.Application.Services;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
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
    private readonly ILogger<LecturesController> _logger;

    public LecturesController(
        LectureSessionService lectureService,
        MatchingService matchingService,
        UploadQueueService queueService,
        ILectureRepository repository,
        IConfiguration configuration,
        IFileWatcher fileWatcher,
        IAuditLogger auditLogger,
        ILogger<LecturesController> logger)
    {
        _lectureService = lectureService;
        _matchingService = matchingService;
        _queueService = queueService;
        _repository = repository;
        _configuration = configuration;
        _fileWatcher = fileWatcher;
        _auditLogger = auditLogger;
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
                session.DriveFolderPath = driveFolderPath.Trim();
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
                session.DriveFolderPath = request.DriveFolderPath.Trim();
                await _repository.UpdateAsync(session);
            }

            // Sync any existing queue entries
            await _queueService.SyncLectureDriveFolderAsync(session);

            // Ensure video or PDF file is enqueued for upload to Google Drive
            var existingQueue = await _queueService.GetByLectureSessionIdAsync(session.LectureSessionId);
            if (!existingQueue.Any())
            {
                var filePath = session.VideoFileLocalPath ?? session.PdfFileLocalPath;
                if (!string.IsNullOrWhiteSpace(filePath) && System.IO.File.Exists(filePath))
                {
                    var isPdf = Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
                    var fileInfo = new FileInfo(filePath);
                    await _queueService.EnqueueFileAsync(
                        session.LectureSessionId,
                        isPdf ? "PDF" : "VIDEO",
                        filePath,
                        fileInfo.Length,
                        session.VideoFileHash ?? session.PdfFileHash,
                        session.DriveFolderPath,
                        allowDuplicate: true);
                }
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
    public async Task<ActionResult<LectureSessionDto>> RematchLecture(string lectureSessionId)
    {
        var lecture = await _repository.GetByIdAsync(lectureSessionId);
        if (lecture == null)
            return NotFound();

        var result = await _matchingService.AnalyzeAndAssignAsync(lecture);
        _logger.LogInformation($"Rematch for {lectureSessionId}: {result.Decision} (confidence: {result.ConfidenceScore}%)");

        var updated = await _repository.GetByIdAsync(lectureSessionId);
        return Ok(MapToDto(updated ?? lecture));
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
