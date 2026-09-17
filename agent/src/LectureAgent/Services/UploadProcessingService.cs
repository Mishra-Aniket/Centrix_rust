namespace LectureAgent.Services;

using LectureAgent.Application.Services;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using LectureAgent.Infrastructure.YouTube;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Background service that processes upload queue and uploads files to Google Drive.
/// </summary>
public class UploadProcessingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<UploadProcessingService> _logger;
    private readonly AgentControlState _controlState;

    public UploadProcessingService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<UploadProcessingService> logger,
        AgentControlState controlState)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
        _controlState = controlState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var checkInterval = int.TryParse(_config["UploadQueue:UploadCheckIntervalSeconds"], out var sec) ? sec : 30;

        _logger.LogInformation($"Upload processing service started (check interval: {checkInterval}s)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_controlState.UploadsPaused)
                {
                    _logger.LogDebug("Upload processing is paused; skipping queue check");
                }
                else
                {
                    await ProcessQueueAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing queue: {ex.Message}");
            }

            // A cancellation here is the host shutting down, not a fault: log noise
            // from this delay previously surfaced as a fatal StopHost error.
            try
            {
                await Task.Delay(checkInterval * 1000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Upload processing service stopped");
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            var maxConcurrent = int.TryParse(_config["UploadQueue:MaxConcurrentUploads"], out var max) ? max : 3;
            List<UploadQueueEntry> pendingUploads;
            using (var scope = _scopeFactory.CreateScope())
            {
                var queueService = scope.ServiceProvider.GetRequiredService<UploadQueueService>();
                pendingUploads = await queueService.GetPendingUploadsAsync(limit: maxConcurrent);
            }

            if (!pendingUploads.Any())
                return;

            _logger.LogInformation($"Processing {pendingUploads.Count} queued uploads");

            var uploadTasks = pendingUploads.Select(entry => ProcessUploadAsync(entry)).ToList();
            await Task.WhenAll(uploadTasks);
        }
        catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Upload queue table is not ready yet; waiting for database initialization to complete.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Upload queue is unavailable yet; waiting for tables to initialize: {ex.Message}");
        }
    }

    private async Task ProcessUploadAsync(UploadQueueEntry entry)
    {
        using var scope = _scopeFactory.CreateScope();
        var queueService = scope.ServiceProvider.GetRequiredService<UploadQueueService>();
        var driveUploader = scope.ServiceProvider.GetRequiredService<IGoogleDriveUploader>();
        var lectureService = scope.ServiceProvider.GetRequiredService<LectureSessionService>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
        var notificationService = scope.ServiceProvider.GetService<INotificationService>();

        try
        {
            _logger.LogInformation($"Processing upload: {entry.QueueEntryId} ({entry.FileType})");

            // Check if should retry
            if (entry.Status == UploadStatus.Failed)
            {
                if (entry.NextRetryAt > DateTime.UtcNow)
                {
                    _logger.LogDebug($"Upload not ready for retry: {entry.QueueEntryId}");
                    return;
                }

                if (entry.RetryCount >= entry.MaxRetries)
                {
                    _logger.LogWarning($"Upload exceeded max retries: {entry.QueueEntryId}");
                    entry.Status = UploadStatus.FailedPermanently;
                    await queueService.SaveAsync(entry);
                    await auditLogger.LogAsync(
                        "UPLOAD_QUEUE", entry.QueueEntryId, "FAILED_PERMANENTLY", null,
                        new { entry.Status }, UploadStatus.FailedPermanently,
                        $"Failed after {entry.MaxRetries} retries");
                    return;
                }
            }

            // Update status to uploading
            entry.Status = UploadStatus.Uploading;
            await queueService.SaveAsync(entry);
            await auditLogger.LogAsync(
                "UPLOAD_QUEUE", entry.QueueEntryId, "UPLOAD_STARTED", null,
                null, new { entry.Status }, "Upload started");

            // Upload file
            try
            {
                if (string.IsNullOrWhiteSpace(entry.DriveFolderPath))
                {
                    var lectureRepo = scope.ServiceProvider.GetService<ILectureRepository>();
                    if (lectureRepo != null)
                    {
                        var session = await lectureRepo.GetByIdAsync(entry.LectureSessionId);
                        if (session != null)
                        {
                            if (!string.IsNullOrWhiteSpace(session.BatchId))
                            {
                                var batchClean = session.BatchId.Replace("/", "-").Replace("\\", "-").Trim();
                                var subjectClean = !string.IsNullOrWhiteSpace(session.SubjectId)
                                    ? session.SubjectId.Replace("/", "-").Replace("\\", "-").Trim()
                                    : null;
                                entry.DriveFolderPath = subjectClean != null ? $"{batchClean}/{subjectClean}" : batchClean;
                            }
                            else
                            {
                                var center = string.IsNullOrWhiteSpace(session.CenterId) ? "Center" : session.CenterId.Replace("/", "-").Replace("\\", "-");
                                var room = string.IsNullOrWhiteSpace(session.RoomId) ? "Room" : session.RoomId.Replace("/", "-").Replace("\\", "-");
                                var date = session.DetectedStartTime != default ? session.DetectedStartTime.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd");
                                var batch = !string.IsNullOrWhiteSpace(session.SubjectId) ? session.SubjectId.Replace("/", "-").Replace("\\", "-") : "ExtraLectures";
                                entry.DriveFolderPath = $"{center}/{room}/{date}/{batch}";
                            }
                            await queueService.SaveAsync(entry);
                        }
                    }
                }

                var fileId = await driveUploader.UploadFileAsync(entry, CancellationToken.None);

                // Verify upload
                var isVerified = await driveUploader.VerifyUploadAsync(fileId, entry.FileHash ?? "");

                if (isVerified)
                {
                    // Mark as successful
                    entry.Status = UploadStatus.Uploaded;
                    entry.DriveFileId = fileId;
                    entry.BytesUploaded = entry.FileSizeBytes;
                    entry.LastError = null;
                    entry.NextRetryAt = null;
                    entry.UpdatedAt = DateTime.UtcNow;
                    await queueService.SaveAsync(entry);

                    // Update lecture status and Drive File ID
                    var lectureRepo = scope.ServiceProvider.GetService<ILectureRepository>();
                    if (lectureRepo != null)
                    {
                        var session = await lectureRepo.GetByIdAsync(entry.LectureSessionId);
                        if (session != null)
                        {
                            if (entry.FileType.Equals("PDF", StringComparison.OrdinalIgnoreCase))
                                session.DrivePdfFileId = fileId;
                            else
                                session.DriveVideoFileId = fileId;

                            session.Status = LectureStatus.Uploaded;
                            session.ReviewStatus = ReviewStatus.Approved;
                            session.UpdatedAt = DateTime.UtcNow;
                            await lectureRepo.UpdateAsync(session);
                            await lectureRepo.SaveChangesAsync();

                            // Auto-trigger sequential local YouTube publish (Drive-to-YouTube streaming) after Drive upload completes
                            if (!entry.FileType.Equals("PDF", StringComparison.OrdinalIgnoreCase) &&
                                _config.GetValue("YouTube:AutoPublishAfterDriveUpload", true))
                            {
                                var youTubeQueue = scope.ServiceProvider.GetService<YouTubePublishQueue>();
                                if (youTubeQueue != null)
                                {
                                    session.YouTubePublishStatus = YouTubePublishStatus.PublishQueued;
                                    await lectureRepo.UpdateAsync(session);
                                    await lectureRepo.SaveChangesAsync();

                                    youTubeQueue.TryEnqueue(new YouTubePublishJob(session.LectureSessionId, YouTubePublishJobKind.Publish));
                                    _logger.LogInformation("Automatically queued local YouTube streaming for lecture {LectureId} after Drive upload completed", session.LectureSessionId);
                                }
                            }
                        }
                    }
                    else
                    {
                        await lectureService.UpdateStatusAsync(
                            entry.LectureSessionId,
                            LectureStatus.Uploaded,
                            "File successfully uploaded to Google Drive");
                    }

                    await auditLogger.LogAsync(
                        "UPLOAD_QUEUE", entry.QueueEntryId, "UPLOAD_COMPLETED", null,
                        null, new { entry.Status, FileId = fileId },
                        $"File uploaded successfully: {fileId}");

                    _logger.LogInformation($"Upload completed: {entry.QueueEntryId}");
                }
                else
                {
                    throw new Exception("Upload verification failed");
                }
            }
            catch (Exception uploadEx)
            {
                _logger.LogWarning($"Upload failed: {uploadEx.Message}");

                // Retry logic
                entry.Status = UploadStatus.Failed;
                entry.RetryCount++;
                entry.LastError = uploadEx.Message;
                entry.NextRetryAt = CalculateNextRetryTime(entry.RetryCount);
                entry.UpdatedAt = DateTime.UtcNow;
                await queueService.SaveAsync(entry);

                await auditLogger.LogAsync(
                    "UPLOAD_QUEUE", entry.QueueEntryId, "UPLOAD_FAILED", null,
                    null, new { entry.Status, entry.RetryCount, entry.NextRetryAt },
                    $"Upload failed (attempt {entry.RetryCount}): {uploadEx.Message}");

                // Notify on repeated failures
                if (entry.RetryCount >= entry.MaxRetries)
                {
                    _logger.LogError($"Upload permanently failed: {entry.QueueEntryId}");
                    if (notificationService != null)
                    {
                        var centerId = _config["Agent:CenterId"] ?? "UNKNOWN";
                        await notificationService.NotifyUploadFailedAsync(centerId, entry, uploadEx.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error processing upload: {ex.Message}");
            entry.LastError = ex.Message;
        }
    }

    private DateTime CalculateNextRetryTime(int attemptNumber)
    {
        var retryDelaySecondsStr = _config["UploadQueue:RetryBackoffSeconds"] ?? "30,120,600,1800,3600";
        var retryDelays = retryDelaySecondsStr.Split(',').Select(s => int.Parse(s.Trim())).ToList();

        var delaySeconds = attemptNumber - 1 < retryDelays.Count
            ? retryDelays[attemptNumber - 1]
            : retryDelays.Last();

        return DateTime.UtcNow.AddSeconds(delaySeconds);
    }
}
