using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;

namespace LectureAgent.Domain.Services;

/// <summary>
/// Interface for file watching and detection.
/// </summary>
public interface IFileWatcher
{
    event EventHandler<FileDetectedEventArgs>? FileDetected;

    void Start(string folderPath);
    void Stop();
    bool IsRunning { get; }

    /// <summary>
    /// Re-enumerates the monitored folder and tracks any media files that are not
    /// already being watched. Returns the number of newly tracked files.
    /// </summary>
    int ScanExisting();
}

/// <summary>
/// Event args when a file is detected.
/// </summary>
public class FileDetectedEventArgs : EventArgs
{
    public string FilePath { get; set; } = null!;
    public string FileType { get; set; } = null!; // VIDEO, PDF
    public long FileSizeBytes { get; set; }
    public DateTime DetectedTime { get; set; }
}

/// <summary>
/// Interface for matching engine.
/// </summary>
public interface IMatchingEngine
{
    Task<MatchingResult> AnalyzeAsync(LectureSession lecture);
}

/// <summary>
/// Result from the matching engine.
/// </summary>
public class MatchingResult
{
    public LectureSession Lecture { get; set; } = null!;
    public TimetableEntry? MatchedSlot { get; set; }
    public int ConfidenceScore { get; set; } // 0-100
    public MatchingDecision Decision { get; set; }
    public string? ReasoningText { get; set; }
    public MatchingScoringDetails? ScoringDetails { get; set; }
    public List<TimetableEntry> Candidates { get; set; } = new();
}

/// <summary>
/// Detailed scoring breakdown.
/// </summary>
public class MatchingScoringDetails
{
    public ScoreFactor RoomFactor { get; set; } = new();
    public ScoreFactor TimeOverlapFactor { get; set; } = new();
    public ScoreFactor DurationFactor { get; set; } = new();
    public ScoreFactor BatchSubjectFactor { get; set; } = new();
    public ScoreFactor TeacherFactor { get; set; } = new();
    public ScoreFactor HistoricalFactor { get; set; } = new();
    public ScoreFactor ContextFactor { get; set; } = new();
}

/// <summary>
/// Individual score factor.
/// </summary>
public class ScoreFactor
{
    public int Score { get; set; }
    public double Weight { get; set; }
    public double Contribution { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// Interface for Google Drive upload.
/// </summary>
public interface IGoogleDriveUploader
{
    Task<string> AuthorizeAsync();
    Task<string> UploadFileAsync(UploadQueueEntry entry, CancellationToken cancellationToken = default);
    Task<bool> VerifyUploadAsync(string fileId, string expectedHash);
    Task CreateFolderStructureAsync(string folderPath);
}

/// <summary>
/// Interface for timetable data provider.
/// </summary>
public interface ITimetableProvider
{
    Task<List<TimetableEntry>> GetTimetableAsync(string centerId, string roomId, DateTime date);
    Task<TimetableOverride?> GetOverrideAsync(string centerId, DateTime date, string slotId);
    Task<List<TimetableOverride>> GetActiveOverridesAsync(string centerId, DateTime date);
    Task SyncTimetableAsync(string centerId);
}

/// <summary>
/// Interface for audit logging.
/// </summary>
public interface IAuditLogger
{
    Task LogAsync(string entityType, string entityId, string actionType, 
                  string? userId, object? oldValues, object? newValues, string? summary);
}

public interface ICloudMetadataSync
{
    Task<bool> SyncAuditAsync(AuditLogEntry auditEntry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Generic repository interface.
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(string id);
    Task<IEnumerable<T>> GetAllAsync();
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(string id);
    Task SaveChangesAsync();
}

public interface ILectureRepository : IRepository<LectureSession>
{
    Task<List<LectureSession>> GetByCenterAndDateAsync(string centerId, DateTime date);
    Task<List<LectureSession>> GetByStatusAsync(LectureStatus status);
    Task<List<LectureSession>> GetPendingReviewAsync(string centerId);
    Task<LectureSession?> GetByVideoLocalPathAsync(string videoLocalPath);
    Task<(List<LectureSession> Items, int TotalCount)> GetPagedLecturesAsync(string? centerId, LectureStatus? status, int offset, int limit);
}

/// <summary>
/// Specialized repository for upload queue entries.
/// </summary>
public interface IUploadQueueRepository : IRepository<UploadQueueEntry>
{
    Task<List<UploadQueueEntry>> GetPendingUploadsAsync(int limit);
    Task<List<UploadQueueEntry>> GetRetryableUploadsAsync();
    Task<UploadQueueEntry?> GetActiveByFileHashAsync(string fileHash);
}

/// <summary>
/// Notification service interface.
/// </summary>
public interface INotificationService
{
    Task NotifyReviewRequiredAsync(string centerId, LectureSession lecture);
    Task NotifyUploadFailedAsync(string centerId, UploadQueueEntry entry, string error);
    Task NotifyMissingLectureAsync(string centerId, TimetableEntry expectedLecture);
}

/// <summary>
/// File validation service.
/// </summary>
public interface IFileValidator
{
    Task<bool> IsFileStableAsync(string filePath, int stabilityCheckMs = 1000);
    Task<(bool Valid, string? Error)> ValidateFileAsync(string filePath);
    Task<string> CalculateHashAsync(string filePath);
    Task<VideoMetadata?> ExtractVideoMetadataAsync(string filePath);
}

/// <summary>
/// Video metadata.
/// </summary>
public class VideoMetadata
{
    public int DurationSeconds { get; set; }
    public string? Codec { get; set; }
    public string? Resolution { get; set; }
    public int? FrameRate { get; set; }
}
