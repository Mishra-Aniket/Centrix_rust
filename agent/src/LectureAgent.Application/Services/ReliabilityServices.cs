namespace LectureAgent.Application.Services;

using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;

/// <summary>
/// Compares the day's timetable against detected recordings and reports slots
/// that finished without any recording, so a missing lecture is surfaced the
/// same day instead of being discovered days later.
/// </summary>
public static class MissingLectureDetector
{
    public static List<MissingSlotInfo> FindMissing(
        IReadOnlyList<TimetableEntry> entries,
        IReadOnlyList<TimetableOverride> activeOverrides,
        IReadOnlyList<LectureSession> lectures,
        DateTime nowLocal,
        int graceMinutes = 30,
        int overlapToleranceMinutes = 15)
    {
        var cancelledSlotIds = activeOverrides
            .Where(o => o.OverrideType == OverrideType.Cancelled)
            .Select(o => o.OriginalSlotId ?? o.OriginalTimetableEntryId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<MissingSlotInfo>();

        foreach (var entry in entries)
        {
            if (cancelledSlotIds.Contains(entry.SlotId) || cancelledSlotIds.Contains(entry.TimetableEntryId))
            {
                continue; // slot was cancelled - it is not supposed to have a recording
            }

            var slotStart = entry.ScheduledDate.Date + entry.SlotStartTime;
            var slotEnd = entry.ScheduledDate.Date + entry.SlotEndTime;

            // Ongoing or not-yet-finished slots cannot be "missing" yet.
            if (slotEnd.AddMinutes(graceMinutes) > nowLocal)
            {
                continue;
            }

            var hasRecording = lectures.Any(lecture =>
                lecture.DetectedStartTime < slotEnd.AddMinutes(overlapToleranceMinutes)
                && lecture.DetectedEndTime > slotStart.AddMinutes(-overlapToleranceMinutes));

            if (!hasRecording)
            {
                missing.Add(new MissingSlotInfo
                {
                    TimetableEntryId = entry.TimetableEntryId,
                    SlotId = entry.SlotId,
                    BatchId = entry.BatchId,
                    SubjectId = entry.SubjectId,
                    TeacherId = entry.TeacherId,
                    ScheduledDate = entry.ScheduledDate,
                    SlotStartTime = entry.SlotStartTime,
                    SlotEndTime = entry.SlotEndTime
                });
            }
        }

        return missing;
    }
}

/// <summary>
/// One timetable slot that finished without a matching recording.
/// </summary>
public class MissingSlotInfo
{
    public string TimetableEntryId { get; set; } = null!;
    public string SlotId { get; set; } = null!;
    public string BatchId { get; set; } = null!;
    public string SubjectId { get; set; } = null!;
    public string? TeacherId { get; set; }
    public DateTime ScheduledDate { get; set; }
    public TimeSpan SlotStartTime { get; set; }
    public TimeSpan SlotEndTime { get; set; }
}

/// <summary>
/// "Already scheduled" style guard (borrowed from PW's studio tracker): when a new
/// recording matches a slot that already has an accepted lecture, the new file is
/// flagged as a duplicate instead of silently uploading a second copy.
/// </summary>
public static class DuplicateSlotChecker
{
    private static readonly HashSet<LectureStatus> AcceptedStatuses = new()
    {
        LectureStatus.AutoAssigned,
        LectureStatus.Confirmed,
        LectureStatus.Uploading,
        LectureStatus.Uploaded,
        LectureStatus.Verified
    };

    public static bool HasAcceptedDuplicate(
        IReadOnlyList<LectureSession> lectures,
        TimetableEntry slot,
        string excludeLectureSessionId,
        int overlapToleranceMinutes = 15)
    {
        var slotStart = slot.ScheduledDate.Date + slot.SlotStartTime;
        var slotEnd = slot.ScheduledDate.Date + slot.SlotEndTime;

        return lectures.Any(lecture =>
            lecture.LectureSessionId != excludeLectureSessionId
            && string.Equals(lecture.BatchId, slot.BatchId, StringComparison.OrdinalIgnoreCase)
            && AcceptedStatuses.Contains(lecture.Status)
            && lecture.DetectedStartTime < slotEnd.AddMinutes(overlapToleranceMinutes)
            && lecture.DetectedEndTime > slotStart.AddMinutes(-overlapToleranceMinutes));
    }
}
