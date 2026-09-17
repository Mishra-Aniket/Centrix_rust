namespace LectureAgent.Services;

using LectureAgent.Application.Services;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Background service that monitors files, matches them to timetable, and queues for upload.
/// </summary>
public class FileMonitoringService : BackgroundService
{
    private readonly IFileWatcher _fileWatcher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<FileMonitoringService> _logger;
    private readonly AgentControlState _controlState;
    private readonly string _organizationId;
    private readonly string _centerId;
    private readonly string _roomId;
    private readonly string _deviceId;
    private readonly bool _queueUnmatchedFiles;
    // Debounce: track files currently being processed to prevent duplicate sessions
    // from rapid OS watcher events (Created + Changed + Changed for same file).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _processingFiles = new(StringComparer.OrdinalIgnoreCase);

    // Persistent tracker for active OBS lecture recordings still being written to disk
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ActiveRecordingState> _activeRecordings = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _watchdogCts;
    private Task? _watchdogTask;

    private sealed class ActiveRecordingState
    {
        public string FilePath { get; set; } = null!;
        public string FileType { get; set; } = "VIDEO";
        public DateTime FirstDetectedUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastSizeChangeUtc { get; set; } = DateTime.UtcNow;
        public long LastSizeBytes { get; set; }
        public DateTime LastLoggedUtc { get; set; } = DateTime.MinValue;
    }

    public FileMonitoringService(
        IFileWatcher fileWatcher,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<FileMonitoringService> logger,
        AgentControlState controlState)
    {
        _fileWatcher = fileWatcher;
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
        _controlState = controlState;
        _organizationId = GetRequiredSetting("Agent:OrganizationId", "ORG_001");
        _centerId = GetRequiredSetting("Agent:CenterId", "CENTER_001");
        _roomId = GetRequiredSetting("Agent:RoomId", "ROOM_101");
        _deviceId = GetRequiredSetting("Agent:DeviceId", Environment.MachineName);
        _queueUnmatchedFiles = bool.TryParse(_config["UploadQueue:QueueUnmatchedFiles"], out var queueUnmatched)
            && queueUnmatched;
    }

    private string GetRequiredSetting(string key, string fallback) =>
        string.IsNullOrWhiteSpace(_config[key]) ? fallback : _config[key]!;

    /// <summary>
    /// Resolves the configured monitor folder to an absolute path, expanding the
    /// "Documents" alias to the user's real Documents folder. Shared with the
    /// agent-info endpoint so the dashboard shows the same folder the watcher uses.
    /// </summary>
    public static string ResolveMonitorFolder(string? rawFolder)
    {
        // Paths pasted from Explorer or typed with surrounding quotes are common; the
        // quotes are not part of the folder name.
        var cleaned = rawFolder?.Trim().Trim('"', '\'').Trim();

        if (string.IsNullOrWhiteSpace(cleaned) ||
            string.Equals(cleaned, "Documents", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cleaned, "MyDocuments", StringComparison.OrdinalIgnoreCase))
        {
            var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docsPath))
                return docsPath;
        }

        var expanded = Environment.ExpandEnvironmentVariables(cleaned ?? "Documents");

        if (string.Equals(expanded, "Documents", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(expanded, "MyDocuments", StringComparison.OrdinalIgnoreCase))
        {
            var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docsPath))
                return docsPath;
        }

        if (Path.IsPathRooted(expanded))
        {
            return expanded;
        }

        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, expanded));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rawFolder = _config["FileWatcher:MonitorFolder"] ?? "Documents";
        var monitorFolder = ResolveMonitorFolder(rawFolder);
        var notesFolder = ResolveOptionalFolder(_config["FileWatcher:NotesFolder"]);
        var enableWatcher = bool.TryParse(_config["FileWatcher:EnableFileWatcher"], out var enable) && enable;

        if (!enableWatcher)
        {
            _logger.LogInformation("File watcher is disabled in configuration");
            return Task.CompletedTask;
        }

        _fileWatcher.FileDetected += OnFileDetected;

        try
        {
            _fileWatcher.Start(monitorFolder);
            if (notesFolder != null)
            {
                _fileWatcher.AddFolder(notesFolder);
            }
            _logger.LogInformation(
                "File monitoring service started. Monitoring: {Folders}",
                notesFolder == null ? monitorFolder : $"{monitorFolder} + notes {notesFolder}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not immediately start file watcher for '{monitorFolder}': {ex.Message}. Will retry in background.");
            _ = StartWatcherRetryLoopAsync(monitorFolder, notesFolder, stoppingToken);
        }

        // Start persistent background watchdog for long OBS recordings
        _watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _watchdogTask = Task.Run(() => ActiveRecordingWatchdogLoopAsync(_watchdogCts.Token), stoppingToken);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves the optional separate notes/PDF folder. Blank means "PDFs live in the
    /// recordings folder itself" and returns null so no second watcher is created.
    /// </summary>
    public static string? ResolveOptionalFolder(string? rawFolder)
    {
        if (string.IsNullOrWhiteSpace(rawFolder))
        {
            return null;
        }

        try
        {
            var resolved = ResolveMonitorFolder(rawFolder);
            return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
        }
        catch
        {
            // A bad path in config must never block the recordings watcher.
            return null;
        }
    }

    private async Task StartWatcherRetryLoopAsync(string monitorFolder, string? notesFolder, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && !_fileWatcher.IsRunning)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                _fileWatcher.Start(monitorFolder);
                if (notesFolder != null)
                {
                    _fileWatcher.AddFolder(notesFolder);
                }
                _logger.LogInformation($"File monitoring service successfully connected on retry. Monitoring: {monitorFolder}");
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Retry connecting watcher to '{monitorFolder}': {ex.Message}");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _fileWatcher.FileDetected -= OnFileDetected;
        _fileWatcher.Stop();
        _watchdogCts?.Cancel();
        if (_watchdogTask != null)
        {
            try { await _watchdogTask; } catch { }
        }
        _logger.LogInformation("File monitoring service stopped");
        await base.StopAsync(cancellationToken);
    }

    private void OnFileDetected(object? sender, FileDetectedEventArgs e)
    {
        if (_controlState.MonitoringPaused)
        {
            _logger.LogInformation($"Monitoring paused; skipping detected file: {e.FilePath}");
            return;
        }

        _ = HandleDetectedFileAsync(e);
    }

    private async Task HandleDetectedFileAsync(FileDetectedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.FilePath) || !File.Exists(e.FilePath))
            return;

        var isVideo = e.FileType.Equals("VIDEO", StringComparison.OrdinalIgnoreCase);

        if (isVideo)
        {
            using var scope = _scopeFactory.CreateScope();
            var fileValidator = scope.ServiceProvider.GetRequiredService<IFileValidator>();

            // Check if file is already completely finished and stable (e.g. copied/dropped in)
            var isStable = await fileValidator.IsFileStableAsync(e.FilePath, stabilityCheckMs: 1500);
            if (!isStable)
            {
                // File is actively being written by OBS or a copy process.
                // Add to persistent watchdog tracker so it will be monitored continuously without timeouts.
                var added = _activeRecordings.TryAdd(e.FilePath, new ActiveRecordingState
                {
                    FilePath = e.FilePath,
                    FileType = e.FileType,
                    FirstDetectedUtc = DateTime.UtcNow,
                    LastSizeChangeUtc = DateTime.UtcNow,
                    LastSizeBytes = e.FileSizeBytes
                });

                if (added)
                {
                    _logger.LogInformation("Active OBS lecture recording detected: {Path}. Monitoring in background until recording finishes...", e.FilePath);
                }
                return;
            }
        }

        // File is stable and complete - process immediately
        await ProcessCompletedFileAsync(e.FilePath, e.FileType, e.FileSizeBytes, e.DetectedTime);
    }

    private async Task ActiveRecordingWatchdogLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OBS active recording watchdog background task active");
        var pollIntervalSeconds = _config.GetValue("FileWatcher:ActiveRecordingPollSeconds", 15);
        if (pollIntervalSeconds < 5) pollIntervalSeconds = 5;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_activeRecordings.IsEmpty)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var fileValidator = scope.ServiceProvider.GetRequiredService<IFileValidator>();

                    foreach (var kvp in _activeRecordings.ToArray())
                    {
                        var filePath = kvp.Key;
                        var state = kvp.Value;

                        if (!File.Exists(filePath))
                        {
                            _activeRecordings.TryRemove(filePath, out _);
                            continue;
                        }

                        FileInfo fileInfo;
                        try
                        {
                            fileInfo = new FileInfo(filePath);
                        }
                        catch
                        {
                            continue;
                        }

                        var currentLength = fileInfo.Length;
                        if (currentLength != state.LastSizeBytes)
                        {
                            state.LastSizeBytes = currentLength;
                            state.LastSizeChangeUtc = DateTime.UtcNow;
                        }

                        var maxHours = _config.GetValue("FileWatcher:MaxRecordingHours", 8);
                        var elapsed = DateTime.UtcNow - state.FirstDetectedUtc;
                        var timeSinceLastChange = DateTime.UtcNow - state.LastSizeChangeUtc;

                        // Timeout safety guard (default 8 hours)
                        if (elapsed.TotalHours > maxHours && timeSinceLastChange.TotalMinutes > 30)
                        {
                            _logger.LogWarning("Recording timed out after {Hours:F1} hours of inactivity: {Path}", elapsed.TotalHours, filePath);
                            _activeRecordings.TryRemove(filePath, out _);
                            continue;
                        }

                        // Check if OBS closed the file handle and length is stable
                        var isStable = await fileValidator.IsFileStableAsync(filePath, stabilityCheckMs: 2000);
                        if (isStable)
                        {
                            if (currentLength >= 1024 * 1024)
                            {
                                if (_activeRecordings.TryRemove(filePath, out _))
                                {
                                    _logger.LogInformation(
                                        "OBS lecture recording finalized and ready! Size: {SizeMB:F1} MB, Active duration: {Elapsed:hh\\:mm\\:ss}. Processing: {Path}",
                                        currentLength / (1024.0 * 1024.0), elapsed, filePath);

                                    _ = ProcessCompletedFileAsync(filePath, state.FileType, currentLength, fileInfo.LastWriteTime);
                                }
                                continue;
                            }
                        }

                        // Status log every 60 seconds
                        if (DateTime.UtcNow - state.LastLoggedUtc > TimeSpan.FromSeconds(60))
                        {
                            state.LastLoggedUtc = DateTime.UtcNow;
                            _logger.LogInformation("OBS recording in progress: {FileName} ({SizeMB:F1} MB written, recording for {Elapsed:hh\\:mm\\:ss})...",
                                Path.GetFileName(filePath), currentLength / (1024.0 * 1024.0), elapsed);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error in OBS active recording watchdog: {Message}", ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessCompletedFileAsync(string filePath, string fileType, long fileSizeBytes, DateTime detectedTime)
    {
        if (!_processingFiles.TryAdd(filePath, 0))
        {
            _logger.LogInformation("File already being processed, skipping duplicate event: {Path}", filePath);
            return;
        }

        try
        {
            _logger.LogInformation($"Processing detected file: {filePath}");

            using var scope = _scopeFactory.CreateScope();
            var fileValidator = scope.ServiceProvider.GetRequiredService<IFileValidator>();
            var lectureService = scope.ServiceProvider.GetRequiredService<LectureSessionService>();
            var matchingService = scope.ServiceProvider.GetRequiredService<MatchingService>();
            var queueService = scope.ServiceProvider.GetRequiredService<UploadQueueService>();

            // Validate finalized file
            var (isValid, error) = await fileValidator.ValidateFileAsync(filePath);
            if (!isValid)
            {
                _logger.LogWarning($"File validation failed: {error}");
                return;
            }

            // Skip files that were already detected on a previous run or rescan
            var lectureRepository = scope.ServiceProvider.GetRequiredService<ILectureRepository>();
            var existingSession = await lectureRepository.GetByFilePathAsync(filePath);
            if (existingSession != null)
            {
                _logger.LogInformation($"File already has lecture session {existingSession.LectureSessionId}; skipping: {filePath}");
                return;
            }

            // Extract file metadata (attempt via ffprobe, otherwise fallback to size estimation)
            int durationSeconds = 60;
            try
            {
                var metadata = await fileValidator.ExtractVideoMetadataAsync(filePath);
                if (metadata != null && metadata.DurationSeconds > 0)
                {
                    durationSeconds = metadata.DurationSeconds;
                }
                else
                {
                    var estimatedMinutes = fileSizeBytes / (2 * 1024 * 1024);
                    durationSeconds = (int)Math.Max(60, estimatedMinutes * 60);
                }
            }
            catch
            {
                var estimatedMinutes = fileSizeBytes / (2 * 1024 * 1024);
                durationSeconds = (int)Math.Max(60, estimatedMinutes * 60);
            }

            var detectedEnd = detectedTime;
            var detectedStart = detectedEnd.AddSeconds(-durationSeconds);

            // Minimum duration gate for video
            var minDurationMinutes = _config.GetValue("FileWatcher:MinimumDurationMinutes", 10);
            if (minDurationMinutes > 0
                && fileType.Equals("VIDEO", StringComparison.OrdinalIgnoreCase)
                && durationSeconds < minDurationMinutes * 60)
            {
                var mins = durationSeconds / 60;
                var secs = durationSeconds % 60;
                _logger.LogInformation(
                    "Recording is {Min}m {Sec}s (< {Threshold} min threshold); skipped from upload: {Path}",
                    mins, secs, minDurationMinutes, filePath);

                var shortSession = await lectureService.CreateSessionAsync(
                    organizationId: _organizationId,
                    centerId: _centerId,
                    roomId: _roomId,
                    deviceId: _deviceId,
                    videoFilePath: filePath,
                    videoFileSize: fileSizeBytes,
                    detectedStart: detectedStart,
                    detectedEnd: detectedEnd);

                await lectureService.UpdateStatusAsync(
                    shortSession.LectureSessionId,
                    LectureStatus.ShortClip,
                    $"Recording duration {mins}m {secs}s is below {minDurationMinutes} min threshold; not uploaded");
                return;
            }

            // Create lecture session
            var session = await lectureService.CreateSessionAsync(
                organizationId: _organizationId,
                centerId: _centerId,
                roomId: _roomId,
                deviceId: _deviceId,
                videoFilePath: filePath,
                videoFileSize: fileSizeBytes,
                detectedStart: detectedStart,
                detectedEnd: detectedEnd);

            _logger.LogInformation($"Created lecture session: {session.LectureSessionId}");

            // Analyze and match against timetable
            var matchResult = await matchingService.AnalyzeAndAssignAsync(session);
            _logger.LogInformation($"Matching result: {matchResult.Decision} (confidence: {matchResult.ConfidenceScore}%)");

            // Duplicate slot check for videos
            if (matchResult.Decision == MatchingDecision.AutoAssigned
                && matchResult.MatchedSlot != null
                && fileType.Equals("VIDEO", StringComparison.OrdinalIgnoreCase))
            {
                var centerLectures = await lectureRepository.GetByCenterAndDateAsync(_centerId, session.DetectedStartTime.Date);
                if (DuplicateSlotChecker.HasAcceptedDuplicate(centerLectures, matchResult.MatchedSlot, session.LectureSessionId))
                {
                    _logger.LogWarning(
                        $"Slot {matchResult.MatchedSlot.SlotId} already has an accepted lecture for {matchResult.MatchedSlot.BatchId}; flagging {session.LectureSessionId} as duplicate");
                    await lectureService.UpdateStatusAsync(
                        session.LectureSessionId,
                        LectureStatus.Duplicate,
                        $"Slot {matchResult.MatchedSlot.SlotId} ({matchResult.MatchedSlot.BatchId}/{matchResult.MatchedSlot.SubjectId}) already has an accepted lecture; not uploading again");
                    return;
                }
            }

            // Cover slide OCR fallback for PDFs: If no timetable match assigned a batch,
            // check if cover slide OCR extracted batch and subject
            if (fileType.Equals("PDF", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(session.BatchId))
            {
                var pdfHints = LectureAgent.Infrastructure.Matching.PdfTextExtractor.Extract(filePath);
                if (pdfHints.HasHints && pdfHints.BatchCodes.Count > 0)
                {
                    session.BatchId = pdfHints.BatchCodes[0];
                    if (pdfHints.Subjects.Count > 0 && string.IsNullOrWhiteSpace(session.SubjectId))
                    {
                        session.SubjectId = pdfHints.Subjects[0];
                    }
                    if (!string.IsNullOrWhiteSpace(pdfHints.TeacherName) && string.IsNullOrWhiteSpace(session.TeacherId))
                    {
                        session.TeacherId = pdfHints.TeacherName;
                    }
                    session.ConfidenceScore = 95;
                    session.Status = LectureStatus.AutoAssigned;
                    session.MatchingReason = "Auto-assigned from whiteboard cover slide OCR";
                    session.DriveFolderPath = !string.IsNullOrWhiteSpace(session.SubjectId)
                        ? $"{session.BatchId}/{session.SubjectId}"
                        : session.BatchId;
                    await lectureRepository.UpdateAsync(session);
                    await lectureRepository.SaveChangesAsync();
                    _logger.LogInformation($"Auto-assigned PDF {session.LectureSessionId} from whiteboard OCR: {session.BatchId}/{session.SubjectId}");
                }
            }

            // Enqueue for upload
            if (matchResult.Decision != MatchingDecision.NoMatch
                || _queueUnmatchedFiles
                || fileType.Equals("PDF", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(session.BatchId))
            {
                if (matchResult.Decision == MatchingDecision.NoMatch && string.IsNullOrWhiteSpace(session.BatchId))
                {
                    session.Status = LectureStatus.ExtraLecture;
                    await lectureService.UpdateStatusAsync(
                        session.LectureSessionId,
                        LectureStatus.ExtraLecture,
                        "Queued without timetable match for testing or manual review");
                }

                if (string.IsNullOrWhiteSpace(session.BatchId))
                {
                    var queueUnmatched = _config.GetValue("UploadQueue:QueueUnmatchedFiles", true);
                    if (!queueUnmatched)
                    {
                        _logger.LogWarning(
                            $"No batch assigned for {session.LectureSessionId}; holding back from upload " +
                            "(assign a batch on the dashboard first)");
                        return;
                    }
                }

                if (string.IsNullOrWhiteSpace(session.DriveFolderPath))
                {
                    session.DriveFolderPath = BuildDriveFolderPath(session, _config);
                }

                var queueEntry = await queueService.EnqueueFileAsync(
                    lectureSessionId: session.LectureSessionId,
                    fileType: fileType,
                    localFilePath: filePath,
                    fileSizeBytes: fileSizeBytes,
                    fileHash: session.VideoFileHash,
                    driveFolderPath: session.DriveFolderPath);

                if (queueEntry == null)
                {
                    await lectureService.UpdateStatusAsync(
                        session.LectureSessionId,
                        LectureStatus.Duplicate,
                        "Identical recording already uploaded (same content hash); skipped duplicate upload");
                    return;
                }

                _logger.LogInformation($"Enqueued file: {queueEntry.QueueEntryId} -> Google Drive folder: {session.DriveFolderPath}");
            }
            else
            {
                _logger.LogInformation($"File not queued (no match): {filePath}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error processing file: {ex.Message}");
        }
        finally
        {
            _processingFiles.TryRemove(filePath, out _);
        }
    }

    private static string BuildDriveFolderPath(LectureSession session, IConfiguration configuration)
    {
        var pattern = configuration["GoogleDrive:FolderStructure"];
        if (string.IsNullOrWhiteSpace(pattern))
        {
            // Default: if BatchId is assigned, place inside {Batch}/{Subject} (or just {Batch} if subject is unknown);
            // otherwise auto-categorize by Center/Room/Date/Extra
            if (!string.IsNullOrWhiteSpace(session.BatchId))
            {
                var batchName = SanitizeSegment(session.BatchId, "Batch");
                return !string.IsNullOrWhiteSpace(session.SubjectId)
                    ? $"{batchName}/{SanitizeSegment(session.SubjectId, "Subject")}"
                    : batchName;
            }

            pattern = "{Center}/{Room}/{Date}/{Batch}";
        }

        var center = SanitizeSegment(session.CenterId, "Center");
        var room = SanitizeSegment(session.RoomId, "Room");
        var date = session.DetectedStartTime != default
            ? session.DetectedStartTime.ToString("yyyy-MM-dd")
            : DateTime.UtcNow.ToString("yyyy-MM-dd");

        var batch = !string.IsNullOrWhiteSpace(session.BatchId)
            ? SanitizeSegment(session.BatchId, "Batch")
            : (!string.IsNullOrWhiteSpace(session.SubjectId)
                ? SanitizeSegment(session.SubjectId, "Extra")
                : "ExtraLectures");

        var subject = !string.IsNullOrWhiteSpace(session.SubjectId)
            ? SanitizeSegment(session.SubjectId, "General")
            : "General";

        if (string.Equals(pattern.Trim(), "{Batch}", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(session.SubjectId) ? $"{batch}/{subject}" : batch;
        }

        return pattern
            .Replace("{Center}", center)
            .Replace("{Room}", room)
            .Replace("{Date}", date)
            .Replace("{Batch}", batch)
            .Replace("{Subject}", subject)
            .Trim('/', '\\');
    }

    private static string SanitizeSegment(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return value.Replace("/", "-").Replace("\\", "-").Trim();
    }
}
