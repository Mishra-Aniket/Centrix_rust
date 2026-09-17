namespace LectureAgent.Application.Services;

using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service for managing lecture session lifecycle.
/// </summary>
public class LectureSessionService
{
    private readonly ILectureRepository _lectureRepository;
    private readonly IFileValidator _fileValidator;
    private readonly IAuditLogger _auditLogger;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LectureSessionService> _logger;

    public LectureSessionService(
        ILectureRepository lectureRepository,
        IFileValidator fileValidator,
        IAuditLogger auditLogger,
        IConfiguration configuration,
        ILogger<LectureSessionService> logger)
    {
        _lectureRepository = lectureRepository;
        _fileValidator = fileValidator;
        _auditLogger = auditLogger;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new lecture session from detected file, or attaches the file to an
    /// existing session when a recording and notes PDF arrive for the same time slot.
    /// </summary>
    public async Task<LectureSession> CreateSessionAsync(
        string organizationId,
        string centerId,
        string roomId,
        string deviceId,
        string videoFilePath,
        long videoFileSize,
        DateTime detectedStart,
        DateTime detectedEnd)
    {
        _logger.LogInformation($"Creating lecture session for {videoFilePath}");

        // Determine file type from extension
        var ext = Path.GetExtension(videoFilePath).ToLowerInvariant();
        var isDocument = ext is ".pdf" or ".pptx" or ".ppt";

        // Try to find an existing session in the same room within a ±15 min window.
        // This allows a video and its matching PDF to be linked to one session.
        var existingSession = await TryFindExistingSessionAsync(centerId, roomId, detectedStart, detectedEnd);
        if (existingSession != null)
        {
            if (isDocument && string.IsNullOrEmpty(existingSession.PdfFileLocalPath))
            {
                existingSession.PdfFileLocalPath = videoFilePath;
                existingSession.PdfFileSizeBytes = videoFileSize;
                existingSession.UpdatedAt = DateTime.UtcNow;
                await _lectureRepository.UpdateAsync(existingSession);
                await _lectureRepository.SaveChangesAsync();
                _logger.LogInformation("Attached PDF/document to existing session {SessionId}: {Path}",
                    existingSession.LectureSessionId, videoFilePath);
                return existingSession;
            }
            else if (!isDocument && string.IsNullOrEmpty(existingSession.VideoFileLocalPath))
            {
                existingSession.VideoFileLocalPath = videoFilePath;
                existingSession.VideoFileSizeBytes = videoFileSize;
                existingSession.UpdatedAt = DateTime.UtcNow;
                await _lectureRepository.UpdateAsync(existingSession);
                await _lectureRepository.SaveChangesAsync();
                _logger.LogInformation("Attached video to existing session {SessionId}: {Path}",
                    existingSession.LectureSessionId, videoFilePath);
                return existingSession;
            }
            // If both slots are filled, fall through to create a new session
        }

        var lectureId = $"LSN-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";
        
        var session = new LectureSession
        {
            LectureSessionId = lectureId,
            OrganizationId = organizationId,
            CenterId = centerId,
            RoomId = roomId,
            DeviceId = deviceId,
            DetectedStartTime = detectedStart,
            DetectedEndTime = detectedEnd,
            DetectedDurationSeconds = (int)(detectedEnd - detectedStart).TotalSeconds,
            Status = LectureStatus.Detected,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Populate the correct file path field based on type
        if (isDocument)
        {
            session.PdfFileLocalPath = videoFilePath;
            session.PdfFileSizeBytes = videoFileSize;
        }
        else
        {
            session.VideoFileLocalPath = videoFilePath;
            session.VideoFileSizeBytes = videoFileSize;
        }

        // Calculate hash
        try
        {
            session.VideoFileHash = await _fileValidator.CalculateHashAsync(videoFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to calculate hash: {ex.Message}");
        }

        await _lectureRepository.AddAsync(session);
        await _lectureRepository.SaveChangesAsync();

        await _auditLogger.LogAsync("LECTURE_SESSION", lectureId, "CREATED", null, null, session, 
            "Lecture session created from file detection");

        return session;
    }

    /// <summary>
    /// Finds an existing session in the same room that overlaps the detected time window.
    /// Used to pair video recordings with their matching PDF/notes files.
    /// </summary>
    private async Task<LectureSession?> TryFindExistingSessionAsync(
        string centerId, string roomId, DateTime detectedStart, DateTime detectedEnd)
    {
        const int toleranceMinutes = 15;
        var sessions = await _lectureRepository.GetByCenterAndDateAsync(centerId, detectedStart.Date);
        return sessions.FirstOrDefault(s =>
            s.RoomId == roomId
            && s.Status != LectureStatus.Cancelled
            && s.Status != LectureStatus.Rejected
            && s.DetectedStartTime < detectedEnd.AddMinutes(toleranceMinutes)
            && s.DetectedEndTime > detectedStart.AddMinutes(-toleranceMinutes));
    }

    /// <summary>
    /// Confirms a lecture assignment.
    /// </summary>
    public async Task<LectureSession> ConfirmAssignmentAsync(
        string lectureSessionId,
        string batchId,
        string subjectId,
        string? teacherId,
        string? reviewerId)
    {
        var session = await _lectureRepository.GetByIdAsync(lectureSessionId);
        if (session == null)
            throw new InvalidOperationException($"Lecture {lectureSessionId} not found");

        var oldValues = new { session.BatchId, session.SubjectId, session.Status, session.ReviewStatus };

        session.BatchId = batchId;
        session.SubjectId = subjectId;
        session.TeacherId = teacherId;
        session.Status = LectureStatus.Confirmed;
        session.ReviewStatus = ReviewStatus.Approved;
        session.ReviewerId = reviewerId;
        session.UpdatedAt = DateTime.UtcNow;
        session.LastStatusChange = DateTime.UtcNow;

        // The batch folder and subject subfolder on Google Drive (e.g. 27-AJ451NA 2026/Physics)
        session.DriveFolderPath = !string.IsNullOrWhiteSpace(subjectId)
            ? $"{batchId.Trim()}/{subjectId.Trim()}"
            : batchId.Trim();

        await _lectureRepository.UpdateAsync(session);
        await _lectureRepository.SaveChangesAsync();

        await _auditLogger.LogAsync("LECTURE_SESSION", lectureSessionId, "CONFIRMED", reviewerId, 
            oldValues, new { session.BatchId, session.SubjectId, session.Status, session.DriveFolderPath }, 
            $"Assignment confirmed: {batchId}/{subjectId}");

        return session;
    }

    /// <summary>
    /// Gets lectures pending review for a center.
    /// </summary>
    public async Task<List<LectureSession>> GetPendingReviewAsync(string centerId)
    {
        return await _lectureRepository.GetPendingReviewAsync(centerId);
    }

    /// <summary>
    /// Updates lecture status.
    /// </summary>
    public async Task<LectureSession> UpdateStatusAsync(
        string lectureSessionId,
        LectureStatus newStatus,
        string? reason = null)
    {
        var session = await _lectureRepository.GetByIdAsync(lectureSessionId);
        if (session == null)
            throw new InvalidOperationException($"Lecture {lectureSessionId} not found");

        var oldStatus = session.Status;
        session.Status = newStatus;
        session.UpdatedAt = DateTime.UtcNow;
        session.LastStatusChange = DateTime.UtcNow;

        await _lectureRepository.UpdateAsync(session);
        await _lectureRepository.SaveChangesAsync();

        await _auditLogger.LogAsync("LECTURE_SESSION", lectureSessionId, "STATUS_UPDATED", null,
            new { Status = oldStatus }, new { Status = newStatus },
            reason ?? $"Status changed from {oldStatus} to {newStatus}");

        return session;
    }
}

/// <summary>
/// Service for matching lectures to timetable slots.
/// </summary>
public class MatchingService
{
    private readonly IMatchingEngine _matchingEngine;
    private readonly ILectureRepository _lectureRepository;
    private readonly INotificationService _notificationService;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<MatchingService> _logger;

    public MatchingService(
        IMatchingEngine matchingEngine,
        ILectureRepository lectureRepository,
        INotificationService notificationService,
        IAuditLogger auditLogger,
        ILogger<MatchingService> logger)
    {
        _matchingEngine = matchingEngine;
        _lectureRepository = lectureRepository;
        _notificationService = notificationService;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    /// <summary>
    /// Analyzes a lecture and determines if it should be auto-assigned.
    /// </summary>
    public async Task<MatchingResult> AnalyzeAndAssignAsync(LectureSession lecture)
    {
        _logger.LogInformation($"Analyzing lecture {lecture.LectureSessionId}");

        var result = await _matchingEngine.AnalyzeAsync(lecture);
        lecture.MatchAttempts++;
        lecture.MatchedAt = DateTime.UtcNow;

        if (result.Decision == MatchingDecision.AutoAssigned && result.MatchedSlot != null)
        {
            _logger.LogInformation($"Auto-assigning {lecture.LectureSessionId} to {result.MatchedSlot.BatchId}/{result.MatchedSlot.SubjectId}");
            
            lecture.BatchId = result.MatchedSlot.BatchId;
            lecture.SubjectId = result.MatchedSlot.SubjectId;
            lecture.TeacherId = result.MatchedSlot.TeacherId;
            lecture.ScheduledStartTime = DateTime.UtcNow.Date + result.MatchedSlot.SlotStartTime;
            lecture.ScheduledEndTime = DateTime.UtcNow.Date + result.MatchedSlot.SlotEndTime;
            lecture.ScheduledSlotId = result.MatchedSlot.SlotId;
            lecture.ConfidenceScore = result.ConfidenceScore;
            lecture.MatchingReason = System.Text.Json.JsonSerializer.Serialize(result.ScoringDetails);
            lecture.AssignmentSource = "AUTO";
            lecture.Status = LectureStatus.AutoAssigned;
            lecture.MatchStatus = MatchStatus.Matched;
            lecture.FailureCode = MatchFailureCode.None;
            lecture.FailureReason = null;
            lecture.UpdatedAt = DateTime.UtcNow;
            lecture.LastStatusChange = DateTime.UtcNow;

            await _lectureRepository.UpdateAsync(lecture);
        }
        else if (result.Decision == MatchingDecision.ReviewRequired)
        {
            _logger.LogInformation($"Review required for {lecture.LectureSessionId}");
            
            if (result.MatchedSlot != null)
            {
                lecture.BatchId = result.MatchedSlot.BatchId;
                lecture.SubjectId = result.MatchedSlot.SubjectId;
                lecture.TeacherId = result.MatchedSlot.TeacherId;
                lecture.ScheduledStartTime = DateTime.UtcNow.Date + result.MatchedSlot.SlotStartTime;
                lecture.ScheduledEndTime = DateTime.UtcNow.Date + result.MatchedSlot.SlotEndTime;
                lecture.ScheduledSlotId = result.MatchedSlot.SlotId;
                lecture.AssignmentSource = "SUGGESTED";
            }

            lecture.Status = LectureStatus.ReviewRequired;
            lecture.ConfidenceScore = result.ConfidenceScore;
            lecture.MatchingReason = System.Text.Json.JsonSerializer.Serialize(result.ScoringDetails);
            lecture.MatchStatus = MatchStatus.Matched;
            lecture.FailureCode = result.FailureCode;
            lecture.FailureReason = null;
            lecture.UpdatedAt = DateTime.UtcNow;
            lecture.LastStatusChange = DateTime.UtcNow;

            await _lectureRepository.UpdateAsync(lecture);
            await _notificationService.NotifyReviewRequiredAsync(lecture.CenterId, lecture);
        }
        else
        {
            lecture.MatchStatus = MatchStatus.NoMatch;
            lecture.FailureCode = result.FailureCode;
            lecture.FailureReason = result.ReasoningText;
            lecture.ConfidenceScore = result.ConfidenceScore;
            lecture.MatchingReason = System.Text.Json.JsonSerializer.Serialize(result.ScoringDetails);
            lecture.Status = LectureStatus.ReviewRequired;
            lecture.UpdatedAt = DateTime.UtcNow;
            lecture.LastStatusChange = DateTime.UtcNow;

            await _lectureRepository.UpdateAsync(lecture);
            await _notificationService.NotifyReviewRequiredAsync(lecture.CenterId, lecture);
        }

        await _lectureRepository.SaveChangesAsync();
        await _auditLogger.LogAsync("LECTURE_SESSION", lecture.LectureSessionId, "ANALYZED", null,
            null, result, $"Matching analysis: {result.Decision} (confidence: {result.ConfidenceScore}%)");

        return result;
    }
}

/// <summary>
/// Service for managing upload queue.
/// </summary>
public class UploadQueueService
{
    private readonly IUploadQueueRepository _queueRepository;
    private readonly IGoogleDriveUploader _driveUploader;
    private readonly ILectureRepository _lectureRepository;
    private readonly IAuditLogger _auditLogger;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UploadQueueService> _logger;

    public UploadQueueService(
        IUploadQueueRepository queueRepository,
        IGoogleDriveUploader driveUploader,
        ILectureRepository lectureRepository,
        IAuditLogger auditLogger,
        IConfiguration configuration,
        ILogger<UploadQueueService> logger)
    {
        _queueRepository = queueRepository;
        _driveUploader = driveUploader;
        _lectureRepository = lectureRepository;
        _auditLogger = auditLogger;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Adds a file to the upload queue.
    /// </summary>
    public async Task<UploadQueueEntry?> EnqueueFileAsync(
        string lectureSessionId,
        string fileType,
        string localFilePath,
        long fileSizeBytes,
        string? fileHash,
        string? driveFolderPath = null,
        bool allowDuplicate = false)
    {
        // Content-level duplicate guard: the same recording saved under a different
        // file name must never upload twice to the same folder unless explicitly requested.
        if (!allowDuplicate && !string.IsNullOrWhiteSpace(fileHash))
        {
            var existing = await _queueRepository.GetActiveByFileHashAsync(fileHash);
            if (existing != null)
            {
                // If it's already in the same target folder (or no specific target folder was given)
                bool sameFolder = string.IsNullOrWhiteSpace(driveFolderPath) 
                    || string.Equals(existing.DriveFolderPath, driveFolderPath, StringComparison.OrdinalIgnoreCase);

                if (sameFolder)
                {
                    if (existing.Status == UploadStatus.Uploaded && !string.IsNullOrWhiteSpace(existing.DriveFileId))
                    {
                        var session = await _lectureRepository.GetByIdAsync(lectureSessionId);
                        if (session != null)
                        {
                            if (fileType == "PDF") session.DrivePdfFileId = existing.DriveFileId;
                            else session.DriveVideoFileId = existing.DriveFileId;
                            session.Status = LectureStatus.Uploaded;
                            session.ReviewStatus = ReviewStatus.Approved;
                            session.UpdatedAt = DateTime.UtcNow;
                            await _lectureRepository.UpdateAsync(session);
                            await _lectureRepository.SaveChangesAsync();
                            _logger.LogInformation(
                                $"Session {lectureSessionId} linked to existing uploaded Drive file {existing.DriveFileId}");
                        }
                    }

                    _logger.LogWarning(
                        $"Duplicate content rejected for {lectureSessionId}: identical file hash already " +
                        $"{existing.Status} as {existing.QueueEntryId} in folder '{existing.DriveFolderPath}'. Not enqueueing again.");
                    return null;
                }
            }
        }

        var queueEntry = new UploadQueueEntry
        {
            QueueEntryId = $"UQ-{Guid.NewGuid().ToString()[..8].ToUpper()}",
            LectureSessionId = lectureSessionId,
            FileType = fileType,
            LocalFilePath = localFilePath,
            FileSizeBytes = fileSizeBytes,
            FileHash = fileHash,
            DriveFolderPath = driveFolderPath,
            Status = UploadStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _queueRepository.AddAsync(queueEntry);
        await _queueRepository.SaveChangesAsync();

        _logger.LogInformation($"Enqueued {fileType} for {lectureSessionId} (Target Drive folder: {driveFolderPath ?? "not assigned"})");

        return queueEntry;
    }

    /// <summary>
    /// Gets pending uploads directly via optimized SQL query.
    /// </summary>
    public async Task<List<UploadQueueEntry>> GetPendingUploadsAsync(int limit = 10)
    {
        return await _queueRepository.GetPendingUploadsAsync(limit);
    }

    /// <summary>
    /// Resets an upload queue entry so the background processor picks it up again,
    /// including entries that exhausted their retries.
    /// </summary>
    public async Task<UploadQueueEntry> RetryAsync(string queueEntryId)
    {
        var entry = await _queueRepository.GetByIdAsync(queueEntryId)
            ?? throw new InvalidOperationException($"Upload queue entry {queueEntryId} not found");

        if (entry.Status == UploadStatus.Uploading)
            throw new InvalidOperationException("Upload is currently in progress; wait for it to finish before retrying");

        if (entry.Status == UploadStatus.Uploaded)
            throw new InvalidOperationException("Upload already completed");

        var oldStatus = entry.Status;
        entry.Status = UploadStatus.Pending;
        entry.RetryCount = 0;
        entry.NextRetryAt = null;
        entry.LastError = null;
        await SaveAsync(entry);

        await _auditLogger.LogAsync("UPLOAD_QUEUE", queueEntryId, "RETRY_REQUESTED", null,
            new { Status = oldStatus }, new { Status = entry.Status },
            $"Manual retry requested (was {oldStatus})");

        return entry;
    }

    /// <summary>
    /// Marks an upload queue entry as cancelled so it is never picked up again.
    /// </summary>
    public async Task<UploadQueueEntry> CancelAsync(string queueEntryId)
    {
        var entry = await _queueRepository.GetByIdAsync(queueEntryId)
            ?? throw new InvalidOperationException($"Upload queue entry {queueEntryId} not found");

        if (entry.Status == UploadStatus.Uploading)
            throw new InvalidOperationException("Upload is currently in progress and cannot be cancelled");

        if (entry.Status == UploadStatus.Uploaded)
            throw new InvalidOperationException("Upload already completed");

        var oldStatus = entry.Status;
        entry.Status = UploadStatus.Cancelled;
        entry.NextRetryAt = null;
        await SaveAsync(entry);

        await _auditLogger.LogAsync("UPLOAD_QUEUE", queueEntryId, "CANCELLED", null,
            new { Status = oldStatus }, new { Status = entry.Status },
            $"Upload cancelled from dashboard (was {oldStatus})");

        return entry;
    }

    /// <summary>
    /// Resets every failed entry (including permanently failed) back to pending.
    /// </summary>
    public async Task<int> RetryAllFailedAsync()
    {
        var failedEntries = await _queueRepository.GetRetryableUploadsAsync();
        var retryCount = 0;

        foreach (var entry in failedEntries)
        {
            entry.Status = UploadStatus.Pending;
            entry.RetryCount = 0;
            entry.NextRetryAt = null;
            entry.LastError = null;
            await SaveAsync(entry);
            retryCount++;
        }

        if (retryCount > 0)
        {
            await _auditLogger.LogAsync("UPLOAD_QUEUE", "BULK", "RETRY_ALL_REQUESTED", null,
                null, new { Count = retryCount },
                $"Bulk retry requested for {retryCount} failed upload(s)");
        }

        return retryCount;
    }

    /// <summary>
    /// Updates target Drive folder for an upload queue entry, resets failure, and triggers retry.
    /// </summary>
    public async Task<UploadQueueEntry> UpdateFolderAndRetryAsync(string queueEntryId, string driveFolderPath, string? batchId = null)
    {
        var entry = await _queueRepository.GetByIdAsync(queueEntryId)
            ?? throw new InvalidOperationException($"Upload queue entry {queueEntryId} not found");

        var oldFolder = entry.DriveFolderPath;
        entry.DriveFolderPath = driveFolderPath.Trim();
        entry.Status = UploadStatus.Pending;
        entry.RetryCount = 0;
        entry.NextRetryAt = null;
        entry.LastError = null;
        entry.UpdatedAt = DateTime.UtcNow;

        await SaveAsync(entry);

        if (!string.IsNullOrEmpty(entry.LectureSessionId))
        {
            var lecture = await _lectureRepository.GetByIdAsync(entry.LectureSessionId);
            if (lecture != null)
            {
                lecture.DriveFolderPath = driveFolderPath.Trim();
                if (!string.IsNullOrWhiteSpace(batchId))
                {
                    lecture.BatchId = batchId.Trim();
                }
                lecture.UpdatedAt = DateTime.UtcNow;
                await _lectureRepository.UpdateAsync(lecture);
                await _lectureRepository.SaveChangesAsync();
            }
        }

        await _auditLogger.LogAsync("UPLOAD_QUEUE", queueEntryId, "FOLDER_UPDATED", null,
            new { OldFolder = oldFolder }, new { NewFolder = entry.DriveFolderPath, BatchId = batchId },
            $"Target Drive folder updated to '{driveFolderPath}' and queued for retry");

        return entry;
    }

    /// <summary>
    /// Syncs Drive folder on existing upload queue entries when lecture assignment is updated or confirmed.
    /// Resets failed uploads to Pending so they immediately retry with the correct folder.
    /// If no queue entry exists yet, enqueues the lecture.
    /// </summary>
    public async Task SyncLectureDriveFolderAsync(LectureSession session)
    {
        var entries = await _queueRepository.GetByLectureSessionIdAsync(session.LectureSessionId);
        if (entries != null && entries.Count > 0)
        {
            foreach (var entry in entries)
            {
                entry.DriveFolderPath = session.DriveFolderPath;
                if (entry.Status == UploadStatus.Failed || entry.Status == UploadStatus.FailedPermanently)
                {
                    entry.Status = UploadStatus.Pending;
                    entry.RetryCount = 0;
                    entry.NextRetryAt = null;
                    entry.LastError = null;
                }
                entry.UpdatedAt = DateTime.UtcNow;
                await SaveAsync(entry);
            }
        }
        else if (!string.IsNullOrWhiteSpace(session.VideoFileLocalPath))
        {
            var ext = System.IO.Path.GetExtension(session.VideoFileLocalPath).ToLowerInvariant();
            var fileType = ext == ".pdf" ? "PDF" : "VIDEO";
            await EnqueueFileAsync(
                session.LectureSessionId,
                fileType,
                session.VideoFileLocalPath,
                session.VideoFileSizeBytes ?? 0,
                session.VideoFileHash,
                session.DriveFolderPath);
        }
    }

    public async Task<List<UploadQueueEntry>> GetByLectureSessionIdAsync(string lectureSessionId)
    {
        return await _queueRepository.GetByLectureSessionIdAsync(lectureSessionId);
    }

    public async Task SaveAsync(UploadQueueEntry queueEntry)
    {
        queueEntry.UpdatedAt = DateTime.UtcNow;
        await _queueRepository.UpdateAsync(queueEntry);
        await _queueRepository.SaveChangesAsync();
    }
}

/// <summary>
/// Service for timetable synchronization.
/// </summary>
public class TimetableSyncService
{
    private readonly ITimetableProvider _timetableProvider;
    private readonly IRepository<TimetableEntry> _timetableRepository;
    private readonly ILogger<TimetableSyncService> _logger;

    public TimetableSyncService(
        ITimetableProvider timetableProvider,
        IRepository<TimetableEntry> timetableRepository,
        ILogger<TimetableSyncService> logger)
    {
        _timetableProvider = timetableProvider;
        _timetableRepository = timetableRepository;
        _logger = logger;
    }

    /// <summary>
    /// Syncs timetable from cloud to local cache.
    /// </summary>
    public async Task SyncTimetableAsync(string centerId)
    {
        _logger.LogInformation($"Syncing timetable for center {centerId}");
        
        try
        {
            await _timetableProvider.SyncTimetableAsync(centerId);
            _logger.LogInformation($"Timetable sync completed for center {centerId}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Timetable sync failed: {ex.Message}");
            throw;
        }
    }
}
