using LectureAgent.Application.Services;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using Xunit;

namespace LectureAgent.Tests;

public class MissingLectureDetectorTests
{
    private static TimetableEntry Slot(string slotId, string batch, DateTime date, string start, string end) => new()
    {
        TimetableEntryId = $"TTE-{slotId}",
        OrganizationId = "ORG",
        CenterId = "Center",
        RoomId = "603",
        ScheduledDate = date,
        SlotStartTime = TimeSpan.Parse(start),
        SlotEndTime = TimeSpan.Parse(end),
        SlotId = slotId,
        BatchId = batch,
        SubjectId = "MATHS"
    };

    private static LectureSession Lecture(DateTime start, DateTime end, string batch = "BATCH-A", LectureStatus status = LectureStatus.Uploaded) => new()
    {
        LectureSessionId = Guid.NewGuid().ToString("N")[..8],
        OrganizationId = "ORG",
        CenterId = "Center",
        RoomId = "603",
        DetectedStartTime = start,
        DetectedEndTime = end,
        BatchId = batch,
        Status = status
    };

    private static readonly DateTime Day = new(2026, 9, 7, 0, 0, 0);

    [Fact]
    public void FinishedSlotWithoutRecording_IsReportedMissing()
    {
        var entries = new List<TimetableEntry> { Slot("S1", "BATCH-A", Day, "09:00", "10:00") };
        var now = Day.AddHours(11); // slot ended an hour ago

        var missing = MissingLectureDetector.FindMissing(entries, new List<TimetableOverride>(), new List<LectureSession>(), now);

        Assert.Single(missing);
        Assert.Equal("S1", missing[0].SlotId);
        Assert.Equal("BATCH-A", missing[0].BatchId);
    }

    [Fact]
    public void OngoingAndFutureSlots_AreNeverReported()
    {
        var entries = new List<TimetableEntry>
        {
            Slot("FUTURE", "BATCH-A", Day, "15:00", "16:00"),
            Slot("ONGOING", "BATCH-A", Day, "10:30", "11:30")
        };
        var now = Day.AddHours(11); // ONGOING ends at 11:30, FUTURE later

        var missing = MissingLectureDetector.FindMissing(entries, new List<TimetableOverride>(), new List<LectureSession>(), now);

        Assert.Empty(missing);
    }

    [Fact]
    public void SlotWithOverlappingRecording_IsNotMissing()
    {
        var entries = new List<TimetableEntry> { Slot("S1", "BATCH-A", Day, "09:00", "10:00") };
        // Recording that started 5 min late and overran by 5 min
        var lectures = new List<LectureSession> { Lecture(Day.AddHours(9).AddMinutes(5), Day.AddHours(10).AddMinutes(5)) };
        var now = Day.AddHours(11);

        var missing = MissingLectureDetector.FindMissing(entries, new List<TimetableOverride>(), lectures, now);

        Assert.Empty(missing);
    }

    [Fact]
    public void CancelledSlot_IsExcluded()
    {
        var entries = new List<TimetableEntry> { Slot("S1", "BATCH-A", Day, "09:00", "10:00") };
        var overrides = new List<TimetableOverride>
        {
            new()
            {
                OverrideId = "OVR-1",
                CenterId = "Center",
                OriginalSlotId = "S1",
                OverrideType = OverrideType.Cancelled,
                EffectiveDate = Day,
                IsActive = true
            }
        };
        var now = Day.AddHours(11);

        var missing = MissingLectureDetector.FindMissing(entries, overrides, new List<LectureSession>(), now);

        Assert.Empty(missing);
    }

    [Fact]
    public void GracePeriod_KeepsRecentSlotUnreported()
    {
        var entries = new List<TimetableEntry> { Slot("S1", "BATCH-A", Day, "09:00", "10:00") };
        var now = Day.AddHours(10).AddMinutes(15); // only 15 min after end (grace is 30)

        var missing = MissingLectureDetector.FindMissing(entries, new List<TimetableOverride>(), new List<LectureSession>(), now);

        Assert.Empty(missing);
    }
}

public class DuplicateSlotCheckerTests
{
    private static TimetableEntry Slot() => new()
    {
        TimetableEntryId = "TTE-1",
        OrganizationId = "ORG",
        CenterId = "Center",
        RoomId = "603",
        ScheduledDate = new DateTime(2026, 9, 7),
        SlotStartTime = TimeSpan.Parse("09:00"),
        SlotEndTime = TimeSpan.Parse("10:00"),
        SlotId = "S1",
        BatchId = "BATCH-A",
        SubjectId = "MATHS"
    };

    private static LectureSession Lecture(string id, DateTime start, DateTime end, string batch = "BATCH-A", LectureStatus status = LectureStatus.Uploaded) => new()
    {
        LectureSessionId = id,
        OrganizationId = "ORG",
        CenterId = "Center",
        RoomId = "603",
        DetectedStartTime = start,
        DetectedEndTime = end,
        BatchId = batch,
        Status = status
    };

    [Fact]
    public void SlotWithAlreadyUploadedSameBatchLecture_IsDuplicate()
    {
        var lectures = new List<LectureSession> { Lecture("LSN-1", new DateTime(2026, 9, 7, 9, 2, 0), new DateTime(2026, 9, 7, 9, 58, 0)) };

        Assert.True(DuplicateSlotChecker.HasAcceptedDuplicate(lectures, Slot(), "LSN-2"));
    }

    [Fact]
    public void SameSlotDifferentBatch_IsNotDuplicate()
    {
        var lectures = new List<LectureSession> { Lecture("LSN-1", new DateTime(2026, 9, 7, 9, 2, 0), new DateTime(2026, 9, 7, 9, 58, 0), batch: "BATCH-B") };

        Assert.False(DuplicateSlotChecker.HasAcceptedDuplicate(lectures, Slot(), "LSN-2"));
    }

    [Fact]
    public void PendingReviewLecture_DoesNotTriggerDuplicate()
    {
        // A lecture still in review is not "accepted" yet - a second recording must not be held back
        var lectures = new List<LectureSession> { Lecture("LSN-1", new DateTime(2026, 9, 7, 9, 2, 0), new DateTime(2026, 9, 7, 9, 58, 0), status: LectureStatus.ReviewRequired) };

        Assert.False(DuplicateSlotChecker.HasAcceptedDuplicate(lectures, Slot(), "LSN-2"));
    }

    [Fact]
    public void ExcludedLecture_DoesNotMatchItself()
    {
        var lectures = new List<LectureSession> { Lecture("LSN-2", new DateTime(2026, 9, 7, 9, 2, 0), new DateTime(2026, 9, 7, 9, 58, 0)) };

        Assert.False(DuplicateSlotChecker.HasAcceptedDuplicate(lectures, Slot(), "LSN-2"));
    }
}
