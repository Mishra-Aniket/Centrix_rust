using LectureAgent.Domain.Enums;

namespace LectureAgent.Domain.Entities;

/// <summary>
/// Represents a recording device at a center.
/// </summary>
public class Device
{
    public string DeviceId { get; set; } = null!;
    public string OrganizationId { get; set; } = null!;
    public string CenterId { get; set; } = null!;
    public string RoomId { get; set; } = null!;

    public string DeviceName { get; set; } = null!;
    public string? DeviceType { get; set; }
    public string RecordingFolderPath { get; set; } = null!;

    public DeviceStatus Status { get; set; } = DeviceStatus.Active;
    public OnlineStatus OnlineStatus { get; set; } = OnlineStatus.Offline;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastHeartbeat { get; set; }
}

/// <summary>
/// Represents an entry in the upload queue.
/// </summary>
public class UploadQueueEntry
{
    public string QueueEntryId { get; set; } = null!;
    public string LectureSessionId { get; set; } = null!;
    public string FileType { get; set; } = null!; // VIDEO, PDF

    public string LocalFilePath { get; set; } = null!;
    public long FileSizeBytes { get; set; }
    public string? FileHash { get; set; }

    public string? DriveFolderPath { get; set; }
    public string? DriveFileName { get; set; }
    public string? DriveFileId { get; set; }
    public string? DriveResumableUri { get; set; }

    public UploadStatus Status { get; set; } = UploadStatus.Pending;
    public long BytesUploaded { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 5;
    public DateTime? NextRetryAt { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int GetProgressPercentage()
    {
        if (FileSizeBytes == 0) return 0;
        return (int)((BytesUploaded / (double)FileSizeBytes) * 100);
    }
}

/// <summary>
/// Represents an audit log entry.
/// </summary>
public class AuditLogEntry
{
    public string AuditEntryId { get; set; } = null!;
    public string OrganizationId { get; set; } = null!;
    public string CenterId { get; set; } = null!;

    public string EntityType { get; set; } = null!; // LECTURE_SESSION, UPLOAD, TIMETABLE
    public string EntityId { get; set; } = null!;
    public string ActionType { get; set; } = null!; // CREATED, UPDATED, CONFIRMED, etc.

    public string? UserId { get; set; }
    public string? UserRole { get; set; }

    public string? OldValues { get; set; } // JSON
    public string? NewValues { get; set; } // JSON
    public string? ChangeSummary { get; set; }

    public bool SyncedToCloud { get; set; } = false;
    public DateTime? CloudSyncAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Configuration entry for the system.
/// </summary>
public class ConfigEntry
{
    public string ConfigKey { get; set; } = null!;
    public string? ConfigValue { get; set; }
    public string DataType { get; set; } = null!; // STRING, INTEGER, BOOLEAN, JSON
    public bool IsOverridable { get; set; } = false;

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Snapshot of device health and upload counters.
/// </summary>
public class DeviceHeartbeatSnapshot
{
    public string DeviceId { get; set; } = null!;
    public string CenterId { get; set; } = null!;
    public string RoomId { get; set; } = null!;
    public string OrganizationId { get; set; } = null!;
    public string AgentVersion { get; set; } = null!;
    public bool Online { get; set; } = true;
    public int PendingUploads { get; set; }
    public int FailedUploads { get; set; }
    public int PendingReviews { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}
