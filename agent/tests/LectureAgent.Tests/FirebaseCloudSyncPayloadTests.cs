using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using LectureAgent.Infrastructure.Cloud;
using Xunit;

namespace LectureAgent.Tests;

/// <summary>
/// Firestore payload shapes, verified without any Firebase connectivity.
/// The reviewer app depends on these field names — especially activeReview.
/// </summary>
public class FirebaseCloudSyncPayloadTests
{
    private static LectureSession MakeLecture(LectureStatus status) => new()
    {
        LectureSessionId = "LSN-20260913-ABCD1234",
        OrganizationId = "ORG_001",
        CenterId = "CENTER-1",
        RoomId = "603",
        DeviceId = "PC-603",
        Status = status,
        ReviewStatus = ReviewStatus.Pending,
        AssignmentSource = "SUGGESTED",
        BatchId = "27-AJ251NA 2026",
        SubjectId = "PHYSICS",
        TeacherId = null,
        ScheduledSlotId = "SLOT-9",
        ConfidenceScore = 72,
        VideoFileLocalPath = "/recordings/AJ251NA_physics.mp4",
        VideoFileSizeBytes = 1_500_000_000,
        DriveFolderPath = "27-AJ251NA 2026",
        DetectedStartTime = new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc),
        DetectedEndTime = new DateTime(2026, 9, 13, 10, 30, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void BuildLectureDocument_CarriesIdentityAndAssignment()
    {
        var document = FirebaseCloudSync.BuildLectureDocument(MakeLecture(LectureStatus.ReviewRequired));

        Assert.Equal("LSN-20260913-ABCD1234", document["lectureSessionId"]);
        Assert.Equal("CENTER-1", document["centerId"]);
        Assert.Equal("ReviewRequired", document["status"]);
        Assert.Equal("27-AJ251NA 2026", document["batchId"]);
        Assert.Equal("PHYSICS", document["subjectId"]);
        Assert.Equal("AJ251NA_physics.mp4", document["videoFileName"]);
        Assert.Equal(72, document["confidenceScore"]);
    }

    [Theory]
    [InlineData(LectureStatus.ReviewRequired, true)]
    [InlineData(LectureStatus.Confirmed, false)]
    [InlineData(LectureStatus.Uploaded, false)]
    [InlineData(LectureStatus.Rejected, false)]
    public void BuildReviewQueueDocument_ActiveReviewReflectsStatus(LectureStatus status, bool expectedActive)
    {
        var document = FirebaseCloudSync.BuildReviewQueueDocument(MakeLecture(status));

        Assert.Equal(expectedActive, document["activeReview"]);
        Assert.Equal("27-AJ251NA 2026", document["suggestedBatchId"]);
        Assert.Equal("PHYSICS", document["suggestedSubjectId"]);
    }

    [Fact]
    public void BuildHeartbeatDocument_CarriesHealthCounters()
    {
        var heartbeat = new DeviceHeartbeatSnapshot
        {
            OrganizationId = "ORG_001",
            CenterId = "CENTER-1",
            RoomId = "603",
            DeviceId = "PC-603",
            AgentVersion = "1.0.0-alpha",
            PendingUploads = 3,
            FailedUploads = 1,
            PendingReviews = 2,
            RecordedAtUtc = new DateTime(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc)
        };

        var document = FirebaseCloudSync.BuildHeartbeatDocument(heartbeat);

        Assert.True((bool)document["online"]!);
        Assert.Equal("PC-603", document["deviceId"]);
        Assert.Equal(3, document["pendingUploads"]);
        Assert.Equal(1, document["failedUploads"]);
        Assert.Equal(2, document["pendingReviews"]);
        Assert.Equal(heartbeat.RecordedAtUtc, document["lastHeartbeat"]);
    }

    [Fact]
    public void BuildAuditDocument_CarriesChangeDetails()
    {
        var entry = new AuditLogEntry
        {
            AuditEntryId = "AUD-1",
            OrganizationId = "ORG_001",
            CenterId = "CENTER-1",
            EntityType = "LECTURE_SESSION",
            EntityId = "LSN-1",
            ActionType = "CONFIRMED",
            UserId = "reviewer@center",
            ChangeSummary = "Assignment confirmed: BATCH/PHYSICS",
            NewValues = "{\"status\":\"Confirmed\"}",
            CreatedAt = new DateTime(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc)
        };

        var document = FirebaseCloudSync.BuildAuditDocument(entry);

        Assert.Equal("AUD-1", document["auditEntryId"]);
        Assert.Equal("LECTURE_SESSION", document["entityType"]);
        Assert.Equal("CONFIRMED", document["actionType"]);
        Assert.Equal("Assignment confirmed: BATCH/PHYSICS", document["summary"]);
    }
}
