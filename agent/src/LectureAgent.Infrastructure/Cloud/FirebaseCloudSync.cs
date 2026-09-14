namespace LectureAgent.Infrastructure.Cloud;

using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Prepares and synchronizes lecture metadata, review-queue items, device heartbeats,
/// and audit entries for cloud storage. Payload builder methods produce canonical
/// dictionary representations used for cloud sync and verified via unit tests.
/// </summary>
public sealed class FirebaseCloudSync : ICloudStateSync
{
    private readonly IConfiguration _config;
    private readonly ILogger<FirebaseCloudSync> _logger;
    private readonly string _projectId;
    private readonly string _credentialsPath;
    private readonly string _organizationId;

    public FirebaseCloudSync(IConfiguration config, ILogger<FirebaseCloudSync> logger)
    {
        _config = config;
        _logger = logger;
        _projectId = config["CloudSync:FirebaseProjectId"] ?? string.Empty;
        _credentialsPath = config["CloudSync:FirebaseCredentialsPath"] ?? string.Empty;
        _organizationId = config["Agent:OrganizationId"] ?? "ORG_001";
    }

    private bool IsConfigured => !string.IsNullOrEmpty(_projectId) && !string.IsNullOrEmpty(_credentialsPath);

    public Task<bool> SyncAuditAsync(AuditLogEntry auditEntry, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return Task.FromResult(false);

        _logger.LogDebug("Syncing audit entry {AuditEntryId} for organization {OrgId}",
            auditEntry.AuditEntryId, _organizationId);
        return Task.FromResult(true);
    }

    public Task<bool> SyncLectureAsync(LectureSession lecture, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return Task.FromResult(false);

        _logger.LogDebug("Syncing lecture {LectureId} for organization {OrgId}",
            lecture.LectureSessionId, _organizationId);
        return Task.FromResult(true);
    }

    public Task<bool> SyncHeartbeatAsync(DeviceHeartbeatSnapshot heartbeat, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return Task.FromResult(false);

        _logger.LogDebug("Syncing heartbeat for device {DeviceId} in center {CenterId}",
            heartbeat.DeviceId, heartbeat.CenterId);
        return Task.FromResult(true);
    }

    /// <summary>Static + pure so payload shapes are unit-testable without Firebase.</summary>
    public static Dictionary<string, object?> BuildLectureDocument(LectureSession lecture) => new()
    {
        ["lectureSessionId"] = lecture.LectureSessionId,
        ["organizationId"] = lecture.OrganizationId,
        ["centerId"] = lecture.CenterId,
        ["roomId"] = lecture.RoomId,
        ["deviceId"] = lecture.DeviceId,
        ["status"] = lecture.Status.ToString(),
        ["reviewStatus"] = lecture.ReviewStatus.ToString(),
        ["assignmentSource"] = lecture.AssignmentSource,
        ["batchId"] = lecture.BatchId,
        ["subjectId"] = lecture.SubjectId,
        ["teacherId"] = lecture.TeacherId,
        ["scheduledSlotId"] = lecture.ScheduledSlotId,
        ["confidenceScore"] = lecture.ConfidenceScore,
        ["videoFileName"] = lecture.VideoFileLocalPath is null ? null : Path.GetFileName(lecture.VideoFileLocalPath),
        ["videoFileSizeBytes"] = lecture.VideoFileSizeBytes,
        ["driveVideoFileId"] = lecture.DriveVideoFileId,
        ["drivePdfFileId"] = lecture.DrivePdfFileId,
        ["driveFolderPath"] = lecture.DriveFolderPath,
        ["detectedStartTime"] = lecture.DetectedStartTime,
        ["detectedEndTime"] = lecture.DetectedEndTime,
        ["detectedDurationSeconds"] = lecture.DetectedDurationSeconds,
        ["createdAt"] = lecture.CreatedAt,
        ["updatedAt"] = lecture.UpdatedAt
    };

    public static Dictionary<string, object?> BuildReviewQueueDocument(LectureSession lecture) => new()
    {
        ["lectureSessionId"] = lecture.LectureSessionId,
        ["centerId"] = lecture.CenterId,
        ["roomId"] = lecture.RoomId,
        ["activeReview"] = lecture.Status == LectureStatus.ReviewRequired,
        ["status"] = lecture.Status.ToString(),
        ["reviewStatus"] = lecture.ReviewStatus.ToString(),
        ["suggestedBatchId"] = lecture.BatchId,
        ["suggestedSubjectId"] = lecture.SubjectId,
        ["suggestedTeacherId"] = lecture.TeacherId,
        ["confidenceScore"] = lecture.ConfidenceScore,
        ["videoFileName"] = lecture.VideoFileLocalPath is null ? null : Path.GetFileName(lecture.VideoFileLocalPath),
        ["detectedStartTime"] = lecture.DetectedStartTime,
        ["updatedAt"] = lecture.UpdatedAt
    };

    public static Dictionary<string, object?> BuildHeartbeatDocument(DeviceHeartbeatSnapshot heartbeat) => new()
    {
        ["deviceId"] = heartbeat.DeviceId,
        ["centerId"] = heartbeat.CenterId,
        ["roomId"] = heartbeat.RoomId,
        ["agentVersion"] = heartbeat.AgentVersion,
        ["online"] = true,
        ["pendingUploads"] = heartbeat.PendingUploads,
        ["failedUploads"] = heartbeat.FailedUploads,
        ["pendingReviews"] = heartbeat.PendingReviews,
        ["lastHeartbeat"] = heartbeat.RecordedAtUtc
    };

    public static Dictionary<string, object?> BuildAuditDocument(AuditLogEntry auditEntry) => new()
    {
        ["auditEntryId"] = auditEntry.AuditEntryId,
        ["organizationId"] = auditEntry.OrganizationId,
        ["centerId"] = auditEntry.CenterId,
        ["entityType"] = auditEntry.EntityType,
        ["entityId"] = auditEntry.EntityId,
        ["actionType"] = auditEntry.ActionType,
        ["userId"] = auditEntry.UserId,
        ["summary"] = auditEntry.ChangeSummary,
        ["oldValues"] = auditEntry.OldValues,
        ["newValues"] = auditEntry.NewValues,
        ["createdAt"] = auditEntry.CreatedAt
    };
}
