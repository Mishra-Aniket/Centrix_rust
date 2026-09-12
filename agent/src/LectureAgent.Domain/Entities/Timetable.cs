using LectureAgent.Domain.Enums;

namespace LectureAgent.Domain.Entities;

/// <summary>
/// Represents a timetable entry for a scheduled lecture.
/// </summary>
public class TimetableEntry
{
    public string TimetableEntryId { get; set; } = null!;
    public string OrganizationId { get; set; } = null!;
    public string CenterId { get; set; } = null!;
    public string RoomId { get; set; } = null!;

    public DateTime ScheduledDate { get; set; }
    public TimeSpan SlotStartTime { get; set; }
    public TimeSpan SlotEndTime { get; set; }
    public string SlotId { get; set; } = null!;

    public string BatchId { get; set; } = null!;
    public string SubjectId { get; set; } = null!;
    public string? TeacherId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a timetable override (cancellation, interchange, etc).
/// </summary>
public class TimetableOverride
{
    public string OverrideId { get; set; } = null!;
    public string OrganizationId { get; set; } = null!;
    public string CenterId { get; set; } = null!;
    public string RoomId { get; set; } = null!;

    public string? OriginalTimetableEntryId { get; set; }
    public DateTime? OriginalDate { get; set; }
    public string? OriginalSlotId { get; set; }

    public OverrideType OverrideType { get; set; }

    // New values (if applicable)
    public string? NewBatchId { get; set; }
    public string? NewSubjectId { get; set; }
    public string? NewTeacherId { get; set; }
    public string? NewRoomId { get; set; }
    public DateTime? NewDate { get; set; }
    public TimeSpan? NewSlotStartTime { get; set; }
    public TimeSpan? NewSlotEndTime { get; set; }
    public string? NewSlotId { get; set; }

    public DateTime EffectiveDate { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AppliedAt { get; set; }
}
