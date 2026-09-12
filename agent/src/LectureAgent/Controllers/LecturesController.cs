namespace LectureAgent.Presentation.Controllers;

using LectureAgent.Application.Services;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// REST API controller for lecture sessions.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LecturesController : ControllerBase
{
    private readonly LectureSessionService _lectureService;
    private readonly MatchingService _matchingService;
    private readonly UploadQueueService _queueService;
    private readonly ILectureRepository _repository;
    private readonly ILogger<LecturesController> _logger;

    public LecturesController(
        LectureSessionService lectureService,
        MatchingService matchingService,
        UploadQueueService queueService,
        ILectureRepository repository,
        ILogger<LecturesController> logger)
    {
        _lectureService = lectureService;
        _matchingService = matchingService;
        _queueService = queueService;
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new lecture session.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<LectureSessionDto>> CreateLecture(
        [FromBody] CreateLectureRequest request)
    {
        _logger.LogInformation($"Creating lecture: {request.VideoFilePath}");

        try
        {
            var session = await _lectureService.CreateSessionAsync(
                request.OrganizationId,
                request.CenterId,
                request.RoomId,
                request.DeviceId,
                request.VideoFilePath,
                request.VideoFileSize,
                request.DetectedStartTime,
                request.DetectedEndTime);

            return CreatedAtAction(nameof(GetLecture), 
                new { lectureSessionId = session.LectureSessionId },
                MapToDto(session));
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating lecture: {ex.Message}");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets a lecture by ID.
    /// </summary>
    [HttpGet("{lectureSessionId}")]
    public async Task<ActionResult<LectureSessionDto>> GetLecture(string lectureSessionId)
    {
        var lecture = await _repository.GetByIdAsync(lectureSessionId);
        if (lecture == null)
            return NotFound();

        return Ok(MapToDto(lecture));
    }

    /// <summary>
    /// Lists lectures with optional filtering.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<object>> ListLectures(
        [FromQuery] string? centerId,
        [FromQuery] string? status,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        LectureStatus? statusEnum = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<LectureStatus>(status, true, out var parsedStatus))
        {
            statusEnum = parsedStatus;
        }

        var (items, totalCount) = await _repository.GetPagedLecturesAsync(centerId, statusEnum, offset, limit);
        var result = items.Select(MapToDto).ToList();

        return Ok(new { total = totalCount, count = result.Count, items = result });
    }

    /// <summary>
    /// Confirms a lecture assignment.
    /// </summary>
    [HttpPut("{lectureSessionId}/confirm")]
    public async Task<ActionResult<LectureSessionDto>> ConfirmLecture(
        string lectureSessionId,
        [FromBody] ConfirmLectureRequest request)
    {
        try
        {
            var session = await _lectureService.ConfirmAssignmentAsync(
                lectureSessionId,
                request.BatchId,
                request.SubjectId,
                request.TeacherId,
                request.ReviewedBy);

            return Ok(MapToDto(session));
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error confirming lecture: {ex.Message}");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Uploads a duplicate-flagged lecture anyway (reviewer override).
    /// </summary>
    [HttpPost("{lectureSessionId}/force-enqueue")]
    public async Task<ActionResult<LectureSessionDto>> ForceEnqueue(string lectureSessionId)
    {
        var lecture = await _repository.GetByIdAsync(lectureSessionId);
        if (lecture == null)
            return NotFound();

        if (lecture.Status != LectureStatus.Duplicate)
            return Conflict(new { error = "Only duplicate-flagged lectures can be force-uploaded" });

        if (string.IsNullOrWhiteSpace(lecture.VideoFileLocalPath))
            return Conflict(new { error = "Lecture has no video file path" });

        var queueEntry = await _queueService.EnqueueFileAsync(
            lectureSessionId: lecture.LectureSessionId,
            fileType: "VIDEO",
            localFilePath: lecture.VideoFileLocalPath,
            fileSizeBytes: lecture.VideoFileSizeBytes ?? 0,
            fileHash: lecture.VideoFileHash,
            driveFolderPath: lecture.DriveFolderPath);

        if (queueEntry == null)
            return Conflict(new { error = "An identical recording is already uploaded or uploading" });

        var updated = await _lectureService.UpdateStatusAsync(
            lectureSessionId,
            LectureStatus.Confirmed,
            "Reviewer chose to upload despite the duplicate flag");

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Re-runs the matching engine for an existing lecture (e.g. after a timetable fix).
    /// </summary>
    [HttpPost("{lectureSessionId}/rematch")]
    public async Task<ActionResult<LectureSessionDto>> RematchLecture(string lectureSessionId)
    {
        var lecture = await _repository.GetByIdAsync(lectureSessionId);
        if (lecture == null)
            return NotFound();

        var result = await _matchingService.AnalyzeAndAssignAsync(lecture);
        _logger.LogInformation($"Rematch for {lectureSessionId}: {result.Decision} (confidence: {result.ConfidenceScore}%)");

        var updated = await _repository.GetByIdAsync(lectureSessionId);
        return Ok(MapToDto(updated ?? lecture));
    }

    /// <summary>
    /// Gets pending reviews for a center.
    /// </summary>
    [HttpGet("review-queue")]
    public async Task<ActionResult<List<LectureSessionDto>>> GetReviewQueue(
        [FromQuery, Required] string centerId)
    {
        var reviews = await _lectureService.GetPendingReviewAsync(centerId);
        return Ok(reviews.Select(MapToDto).ToList());
    }

    private static LectureSessionDto MapToDto(LectureSession lecture)
    {
        return new LectureSessionDto
        {
            LectureSessionId = lecture.LectureSessionId,
            OrganizationId = lecture.OrganizationId,
            CenterId = lecture.CenterId,
            RoomId = lecture.RoomId,
            DeviceId = lecture.DeviceId,
            DetectedStartTime = lecture.DetectedStartTime,
            DetectedEndTime = lecture.DetectedEndTime,
            DetectedDurationSeconds = lecture.DetectedDurationSeconds,
            BatchId = lecture.BatchId,
            SubjectId = lecture.SubjectId,
            TeacherId = lecture.TeacherId,
            ConfidenceScore = lecture.ConfidenceScore,
            Status = lecture.Status.ToString(),
            ReviewStatus = lecture.ReviewStatus.ToString(),
            CreatedAt = lecture.CreatedAt,
            UpdatedAt = lecture.UpdatedAt
        };
    }
}

/// <summary>
/// DTO for lecture session.
/// </summary>
public class LectureSessionDto
{
    public string LectureSessionId { get; set; } = null!;
    public string OrganizationId { get; set; } = null!;
    public string CenterId { get; set; } = null!;
    public string RoomId { get; set; } = null!;
    public string DeviceId { get; set; } = null!;
    public DateTime DetectedStartTime { get; set; }
    public DateTime DetectedEndTime { get; set; }
    public int DetectedDurationSeconds { get; set; }
    public string? BatchId { get; set; }
    public string? SubjectId { get; set; }
    public string? TeacherId { get; set; }
    public int ConfidenceScore { get; set; }
    public string Status { get; set; } = null!;
    public string ReviewStatus { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Request to create a lecture.
/// </summary>
public class CreateLectureRequest
{
    [Required]
    public string OrganizationId { get; set; } = null!;

    [Required]
    public string CenterId { get; set; } = null!;

    [Required]
    public string RoomId { get; set; } = null!;

    [Required]
    public string DeviceId { get; set; } = null!;

    [Required]
    public string VideoFilePath { get; set; } = null!;

    [Required, Range(1, long.MaxValue)]
    public long VideoFileSize { get; set; }

    [Required]
    public DateTime DetectedStartTime { get; set; }

    [Required]
    public DateTime DetectedEndTime { get; set; }
}

/// <summary>
/// Request to confirm a lecture.
/// </summary>
public class ConfirmLectureRequest
{
    [Required]
    public string BatchId { get; set; } = null!;

    [Required]
    public string SubjectId { get; set; } = null!;

    public string? TeacherId { get; set; }

    public string? ReviewedBy { get; set; }
}
