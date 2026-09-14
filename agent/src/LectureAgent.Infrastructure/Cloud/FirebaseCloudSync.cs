namespace LectureAgent.Infrastructure.Cloud;

using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Pushes lecture metadata, review-queue items, device heartbeats and audit entries
/// to Firestore. Every write is an upsert keyed by the local entity id, so retries
/// and repeated sync cycles are always safe.
/// Config: CloudSync:FirebaseProjectId + CloudSync:FirebaseCredentialsPath
/// (a Firebase service-account JSON with Firestore write access).
/// </summary>
public sealed class FirebaseCloudSync : ICloudStateSync
{
    private readonly IConfiguration _config;
    private readonly ILogger<FirebaseCloudSync> _logger;
    private readonly string _projectId;
    private readonly string _credentialsPath;
    private readonly string _organizationId;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private FirestoreDb? _db;

    public FirebaseCloudSync(IConfiguration config, ILogger<FirebaseCloudSync> logger)
    {
        _config = config;
        _logger = logger;
        _projectId = config["CloudSync:FirebaseProjectId"] ?? string.Empty;
        _credentialsPath = config["CloudSync:FirebaseCredentialsPath"] ?? string.Empty;
        _organizationId = config["Agent:OrganizationId"] ?? "ORG_001";
    }

    private bool IsConfigured => _projectId.Length > 0 && _credentialsPath.Length > 0;

    public async Task<bool> SyncAuditAsync(AuditLogEntry auditEntry, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return false;

        try
        {
            var db = await GetDbAsync(cancellationToken);
            var docId = string.IsNullOrWhiteSpace(auditEntry.AuditEntryId)
                ? $"AUD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..6].ToUpper()}"
                : auditEntry.AuditEntryId;

            var doc = db.Collection("organizations").Document(_organizationId)
                .Collection("auditLog").Document(docId);

            await doc.SetAsync(BuildAuditDocument(auditEntry), cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Firebase audit sync failed: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<bool> SyncLectureAsync(LectureSession lecture, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return false;

        try
        {
            var db = await GetDbAsync(cancellationToken);
            var organization = db.Collection("organizations").Document(_organizationId);

            var lectureDoc = organization.Collection("lectures").Document(lecture.LectureSessionId);
            await lectureDoc.SetAsync(BuildLectureDocument(lecture), cancellationToken: cancellationToken);

            // Mirror into the review queue collection; activeReview tells the reviewer
            // app whether this still needs a human. Upsert keeps resolution history.
            var reviewDoc = organization.Collection("reviewQueue").Document(lecture.LectureSessionId);
            await reviewDoc.SetAsync(BuildReviewQueueDocument(lecture), cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Firebase lecture sync failed for {LectureId}: {Message}",
                lecture.LectureSessionId, ex.Message);
            return false;
        }
    }

    public async Task<bool> SyncHeartbeatAsync(DeviceHeartbeatSnapshot heartbeat, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return false;

        try
        {
            var db = await GetDbAsync(cancellationToken);
            var doc = db.Collection("organizations").Document(_organizationId)
                .Collection("centers").Document(heartbeat.CenterId)
                .Collection("devices").Document(heartbeat.DeviceId);

            await doc.SetAsync(BuildHeartbeatDocument(heartbeat), cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Firebase heartbeat sync failed: {Message}", ex.Message);
            return false;
        }
    }

    private async Task<FirestoreDb> GetDbAsync(CancellationToken cancellationToken)
    {
        if (_db != null)
            return _db;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_db != null)
                return _db;

            // Managed gRPC transport (Grpc.Net.Client) — no native dependencies.
            var client = await new FirestoreClientBuilder
            {
                CredentialsPath = _credentialsPath
            }.BuildAsync(cancellationToken);

            _db = FirestoreDb.Create(_projectId, client: client);
            _logger.LogInformation("Firestore sync initialized for project {ProjectId}", _projectId);
            return _db;
        }
        finally
        {
            _initLock.Release();
        }
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
