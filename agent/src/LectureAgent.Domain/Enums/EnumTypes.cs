namespace LectureAgent.Domain.Enums;

/// <summary>
/// Represents the lifecycle status of a lecture session.
/// </summary>
public enum LectureStatus
{
    Detected = 1,
    Processing = 2,
    AutoAssigned = 3,
    ReviewRequired = 4,
    Confirmed = 5,
    Uploading = 6,
    Uploaded = 7,
    Verified = 8,
    PdfPending = 9,
    VideoMissing = 10,
    PdfMissing = 11,
    ExtraLecture = 12,
    Cancelled = 13,
    UploadFailed = 14,
    CorrectionRequired = 15,
    Corrected = 16,
    Rejected = 17,
    Archived = 18,
    Duplicate = 19
}

/// <summary>
/// Represents the review status of a lecture.
/// </summary>
public enum ReviewStatus
{
    Pending = 1,
    Locked = 2,
    Approved = 3,
    Rejected = 4
}

/// <summary>
/// Represents the upload status of a file to Google Drive.
/// </summary>
public enum UploadStatus
{
    Pending = 1,
    Uploading = 2,
    Uploaded = 3,
    Failed = 4,
    FailedPermanently = 5,
    Cancelled = 6
}

/// <summary>
/// Represents the decision result from the matching engine.
/// </summary>
public enum MatchingDecision
{
    AutoAssigned = 1,
    ReviewRequired = 2,
    NoMatch = 3
}

/// <summary>
/// Device status in the system.
/// </summary>
public enum DeviceStatus
{
    Active = 1,
    Disabled = 2
}

/// <summary>
/// Online/offline status of a device.
/// </summary>
public enum OnlineStatus
{
    Online = 1,
    Offline = 2
}

/// <summary>
/// Timetable override type.
/// </summary>
public enum OverrideType
{
    Cancelled = 1,
    Interchanged = 2,
    Extra = 3,
    ChangedBatch = 4,
    ChangedSubject = 5,
    ChangedTeacher = 6,
    ChangedRoom = 7,
    ChangedTime = 8
}
