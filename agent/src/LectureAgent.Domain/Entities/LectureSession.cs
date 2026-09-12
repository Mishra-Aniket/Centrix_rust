using LectureAgent.Domain.Enums;

namespace LectureAgent.Domain.Entities;

/// <summary>
/// Represents a recorded lecture session with metadata and assignment information.
/// </summary>
public class LectureSession
{
    public string LectureSessionId { get; set; } = null!;
    public string OrganizationId { get; set; } = null!;
    public string CenterId { get; set; } = null!;
    public string RoomId { get; set; } = null!;
    public string DeviceId { get; set; } = null!;

    // Detected timing
    public DateTime DetectedStartTime { get; set; }
    public DateTime DetectedEndTime { get; set; }
    public int DetectedDurationSeconds { get; set; }

    // Scheduled (from timetable)
    public DateTime? ScheduledStartTime { get; set; }
    public DateTime? ScheduledEndTime { get; set; }
    public string? ScheduledSlotId { get; set; }

    // Assignment
    public string? BatchId { get; set; }
    public string? SubjectId { get; set; }
    public string? TeacherId { get; set; }
    public string AssignmentSource { get; set; } = "MANUAL"; // AUTO, MANUAL, TIMETABLE
    public int ConfidenceScore { get; set; } // 0-100
    public string? MatchingReason { get; set; } // JSON

    // Files
    public string? VideoFileLocalPath { get; set; }
    public long? VideoFileSizeBytes { get; set; }
    public string? VideoFileHash { get; set; }
    public string? PdfFileLocalPath { get; set; }
    public long? PdfFileSizeBytes { get; set; }
    public string? PdfFileHash { get; set; }

    // Cloud references
    public string? DriveVideoFileId { get; set; }
    public string? DrivePdfFileId { get; set; }
    public string? DriveFolderPath { get; set; }

    // Status lifecycle
    public LectureStatus Status { get; set; } = LectureStatus.Detected;
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;
    public string? ReviewerId { get; set; }
    public DateTime? LockedAt { get; set; }
    public string? LockedBy { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastStatusChange { get; set; }

    /// <summary>
    /// Calculates the duration of the detected lecture in minutes.
    /// </summary>
    public double GetDurationMinutes() => DetectedDurationSeconds / 60.0;

    /// <summary>
    /// Calculates the overlap percentage between detected and scheduled times.
    /// </summary>
    public double GetTimeOverlapPercentage()
    {
        if (ScheduledStartTime == null || ScheduledEndTime == null)
            return 0;

        var overlapStart = new[] { DetectedStartTime, ScheduledStartTime.Value }.Max();
        var overlapEnd = new[] { DetectedEndTime, ScheduledEndTime.Value }.Min();

        if (overlapEnd <= overlapStart)
            return 0;

        var overlapDuration = (overlapEnd - overlapStart).TotalSeconds;
        var scheduledDuration = (ScheduledEndTime.Value - ScheduledStartTime.Value).TotalSeconds;

        return (overlapDuration / scheduledDuration) * 100;
    }

    /// <summary>
    /// Determines if this lecture is marked as complete.
    /// </summary>
    public bool IsComplete() => Status == LectureStatus.Verified;
}
