namespace LectureAgent.Services;

using LectureAgent.Application.Services;
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
        if (string.IsNullOrWhiteSpace(rawFolder) ||
            string.Equals(rawFolder, "Documents", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawFolder, "MyDocuments", StringComparison.OrdinalIgnoreCase))
        {
            var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docsPath))
                return docsPath;
        }

        var expanded = Environment.ExpandEnvironmentVariables(rawFolder ?? "Documents");

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
        var enableWatcher = bool.TryParse(_config["FileWatcher:EnableFileWatcher"], out var enable) && enable;

        if (!enableWatcher)
        {
            _logger.LogInformation("File watcher is disabled in configuration");
            return Task.CompletedTask;
        }

        try
        {
            // Subscribe to file detected events
            _fileWatcher.FileDetected += OnFileDetected;

            // Start watching
            _fileWatcher.Start(monitorFolder);
            _logger.LogInformation($"File monitoring service started. Monitoring: {monitorFolder}");

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to start file monitoring: {ex.Message}");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _fileWatcher.FileDetected -= OnFileDetected;
        _fileWatcher.Stop();
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

        // Fire and forget - process file asynchronously
        _ = ProcessFileAsync(e);
    }

    private async Task ProcessFileAsync(FileDetectedEventArgs e)
    {
        // Debounce: prevent concurrent processing of the same file path.
        // OS file watchers often fire Created+Changed+Changed rapidly for one file.
        if (!_processingFiles.TryAdd(e.FilePath, 0))
        {
            _logger.LogInformation("File already being processed, skipping duplicate event: {Path}", e.FilePath);
            return;
        }

        try
        {
            _logger.LogInformation($"Processing detected file: {e.FilePath}");

            using var scope = _scopeFactory.CreateScope();
            var fileValidator = scope.ServiceProvider.GetRequiredService<IFileValidator>();
            var lectureService = scope.ServiceProvider.GetRequiredService<LectureSessionService>();
            var matchingService = scope.ServiceProvider.GetRequiredService<MatchingService>();
            var queueService = scope.ServiceProvider.GetRequiredService<UploadQueueService>();

            // Validate file
            var (isValid, error) = await fileValidator.ValidateFileAsync(e.FilePath);
            if (!isValid)
            {
                _logger.LogWarning($"File validation failed: {error}");
                return;
            }

            // Wait for file to be stable (retry for up to 60 seconds for recordings still being written)
            const int maxStabilityRetries = 12;
            const int stabilityDelayMs = 5000;
            var isStable = false;
            for (int attempt = 1; attempt <= maxStabilityRetries; attempt++)
            {
                isStable = await fileValidator.IsFileStableAsync(e.FilePath);
                if (isStable) break;
                if (attempt == maxStabilityRetries)
                {
                    _logger.LogWarning("File not stable after {Attempts} attempts ({TotalSec}s), skipping: {Path}",
                        maxStabilityRetries, maxStabilityRetries * stabilityDelayMs / 1000, e.FilePath);
                    return;
                }
                _logger.LogInformation("File not yet stable (attempt {Attempt}/{Max}), waiting {Delay}ms: {Path}",
                    attempt, maxStabilityRetries, stabilityDelayMs, e.FilePath);
                await Task.Delay(stabilityDelayMs);
            }

            // Skip files that were already detected on a previous run or rescan,
            // otherwise every restart would re-create sessions for the same recordings.
            var lectureRepository = scope.ServiceProvider.GetRequiredService<ILectureRepository>();
            var existingSession = await lectureRepository.GetByVideoLocalPathAsync(e.FilePath);
            if (existingSession != null)
            {
                _logger.LogInformation($"File already has lecture session {existingSession.LectureSessionId}; skipping: {e.FilePath}");
                return;
            }

            // Extract file metadata (attempt via ffprobe, otherwise fallback to size estimation)
            int durationSeconds = 60;
            try
            {
                var metadata = await fileValidator.ExtractVideoMetadataAsync(e.FilePath);
                if (metadata != null && metadata.DurationSeconds > 0)
                {
                    durationSeconds = metadata.DurationSeconds;
                }
                else
                {
                    var estimatedMinutes = e.FileSizeBytes / (2 * 1024 * 1024);
                    durationSeconds = (int)Math.Max(60, estimatedMinutes * 60);
                }
            }
            catch
            {
                var estimatedMinutes = e.FileSizeBytes / (2 * 1024 * 1024);
                durationSeconds = (int)Math.Max(60, estimatedMinutes * 60);
            }

            // A finished recording file's timestamp marks when recording ended
            var detectedEnd = e.DetectedTime;
            var detectedStart = detectedEnd.AddSeconds(-durationSeconds);

            // Create lecture session
            var session = await lectureService.CreateSessionAsync(
                organizationId: _organizationId,
                centerId: _centerId,
                roomId: _roomId,
                deviceId: _deviceId,
                videoFilePath: e.FilePath,
                videoFileSize: e.FileSizeBytes,
                detectedStart: detectedStart,
                detectedEnd: detectedEnd);

            _logger.LogInformation($"Created lecture session: {session.LectureSessionId}");

            // Analyze and match
            var matchResult = await matchingService.AnalyzeAndAssignAsync(session);
            _logger.LogInformation($"Matching result: {matchResult.Decision} (confidence: {matchResult.ConfidenceScore}%)");

            // "Already scheduled" guard: if this slot already has an accepted lecture,
            // hold the new video as a duplicate instead of silently uploading it twice.
            // Notes/PDFs share the slot with the lecture video, so they are exempt.
            if (matchResult.Decision == MatchingDecision.AutoAssigned
                && matchResult.MatchedSlot != null
                && e.FileType.Equals("VIDEO", StringComparison.OrdinalIgnoreCase))
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

            // Enqueue for upload only when the lecture has a batch - the Drive folder
            // is the pre-created batch folder, so an unbatched file has nowhere to go.
            if (matchResult.Decision != MatchingDecision.NoMatch
                || _queueUnmatchedFiles
                || e.FileType.Equals("PDF", StringComparison.OrdinalIgnoreCase))
            {
                if (matchResult.Decision == MatchingDecision.NoMatch)
                {
                    session.Status = LectureStatus.ExtraLecture;
                    await lectureService.UpdateStatusAsync(
                        session.LectureSessionId,
                        LectureStatus.ExtraLecture,
                        "Queued without timetable match for testing or manual review");
                }

                if (string.IsNullOrWhiteSpace(session.BatchId))
                {
                    _logger.LogWarning(
                        $"No batch assigned for {session.LectureSessionId}; holding back from upload " +
                        "(batch folders on Drive already exist - assign a batch on the dashboard first)");
                    return;
                }

                session.DriveFolderPath = session.BatchId;

                var queueEntry = await queueService.EnqueueFileAsync(
                    lectureSessionId: session.LectureSessionId,
                    fileType: e.FileType,
                    localFilePath: e.FilePath,
                    fileSizeBytes: e.FileSizeBytes,
                    fileHash: session.VideoFileHash,
                    driveFolderPath: session.DriveFolderPath);

                if (queueEntry == null)
                {
                    // Identical content already queued/uploaded - never upload twice.
                    await lectureService.UpdateStatusAsync(
                        session.LectureSessionId,
                        LectureStatus.Duplicate,
                        "Identical recording already uploaded (same content hash); skipped duplicate upload");
                    return;
                }

                _logger.LogInformation($"Enqueued file: {queueEntry.QueueEntryId} -> Google Drive batch folder: {session.DriveFolderPath}");
            }
            else
            {
                _logger.LogInformation($"File not queued (no match): {e.FilePath}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error processing file: {ex.Message}");
        }
        finally
        {
            _processingFiles.TryRemove(e.FilePath, out _);
        }
    }
}
