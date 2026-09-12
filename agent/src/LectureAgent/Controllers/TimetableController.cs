using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace LectureAgent.Presentation.Controllers;

[ApiController]
[Route("api/timetable")]
public sealed class TimetableController : ControllerBase
{
    private readonly LectureContext _dbContext;

    public TimetableController(LectureContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<List<TimetableEntry>>> List(
        [FromQuery, Required] string centerId,
        [FromQuery, Required] string roomId,
        [FromQuery] DateTime? date)
    {
        var requestedDate = (date ?? DateTime.UtcNow).Date;
        var nextDate = requestedDate.AddDays(1);
        var entries = await _dbContext.TimetableEntries
            .AsNoTracking()
            .Where(entry => entry.CenterId == centerId
                && entry.RoomId == roomId
                && entry.ScheduledDate >= requestedDate
                && entry.ScheduledDate < nextDate)
            .ToListAsync();

        return Ok(entries.OrderBy(entry => entry.SlotStartTime).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<TimetableEntry>> Create([FromBody] CreateTimetableEntryRequest request)
    {
        var entry = new TimetableEntry
        {
            TimetableEntryId = $"TTE-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
            OrganizationId = request.OrganizationId,
            CenterId = request.CenterId,
            RoomId = request.RoomId,
            ScheduledDate = request.ScheduledDate.Date,
            SlotStartTime = request.SlotStartTime,
            SlotEndTime = request.SlotEndTime,
            SlotId = request.SlotId,
            BatchId = request.BatchId,
            SubjectId = request.SubjectId,
            TeacherId = request.TeacherId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (entry.SlotEndTime <= entry.SlotStartTime)
            return BadRequest(new { error = "SlotEndTime must be later than SlotStartTime" });

        await _dbContext.TimetableEntries.AddAsync(entry);
        await _dbContext.SaveChangesAsync();
        return Created($"/api/timetable/{entry.TimetableEntryId}", entry);
    }

    [HttpPost("overrides")]
    public async Task<ActionResult<TimetableOverride>> CreateOverride([FromBody] CreateTimetableOverrideRequest request)
    {
        var timetableOverride = new TimetableOverride
        {
            OverrideId = $"OVR-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
            OrganizationId = request.OrganizationId,
            CenterId = request.CenterId,
            RoomId = request.RoomId,
            OriginalTimetableEntryId = request.OriginalTimetableEntryId,
            OriginalDate = request.OriginalDate?.Date,
            OriginalSlotId = request.OriginalSlotId,
            OverrideType = request.OverrideType,
            NewBatchId = request.NewBatchId,
            NewSubjectId = request.NewSubjectId,
            NewTeacherId = request.NewTeacherId,
            NewRoomId = request.NewRoomId,
            NewDate = request.NewDate?.Date,
            NewSlotStartTime = request.NewSlotStartTime,
            NewSlotEndTime = request.NewSlotEndTime,
            NewSlotId = request.NewSlotId,
            EffectiveDate = request.EffectiveDate.Date,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dbContext.TimetableOverrides.AddAsync(timetableOverride);
        await _dbContext.SaveChangesAsync();
        return Created($"/api/timetable/overrides/{timetableOverride.OverrideId}", timetableOverride);
    }

    /// <summary>
    /// Lists timetable overrides for a day (active only by default).
    /// </summary>
    [HttpGet("overrides")]
    public async Task<ActionResult<List<TimetableOverride>>> ListOverrides(
        [FromQuery, Required] string centerId,
        [FromQuery] DateTime? date,
        [FromQuery] string? roomId,
        [FromQuery] bool includeInactive = false)
    {
        var requestedDate = (date ?? DateTime.UtcNow).Date;
        var nextDate = requestedDate.AddDays(1);

        var query = _dbContext.TimetableOverrides
            .AsNoTracking()
            .Where(item => item.CenterId == centerId
                && item.EffectiveDate >= requestedDate
                && item.EffectiveDate < nextDate);

        if (!string.IsNullOrWhiteSpace(roomId))
            query = query.Where(item => item.RoomId == roomId);

        if (!includeInactive)
            query = query.Where(item => item.IsActive);

        var items = await query
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>
    /// Deactivates an override (undo). Idempotent.
    /// </summary>
    [HttpPost("overrides/{overrideId}/deactivate")]
    public async Task<ActionResult<TimetableOverride>> DeactivateOverride(string overrideId)
    {
        var timetableOverride = await _dbContext.TimetableOverrides
            .FirstOrDefaultAsync(item => item.OverrideId == overrideId);

        if (timetableOverride == null)
            return NotFound();

        if (timetableOverride.IsActive)
        {
            timetableOverride.IsActive = false;
            timetableOverride.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        return Ok(timetableOverride);
    }
}

public sealed class CreateTimetableEntryRequest
{
    [Required] public string OrganizationId { get; set; } = null!;
    [Required] public string CenterId { get; set; } = null!;
    [Required] public string RoomId { get; set; } = null!;
    [Required] public DateTime ScheduledDate { get; set; }
    [Required] public TimeSpan SlotStartTime { get; set; }
    [Required] public TimeSpan SlotEndTime { get; set; }
    [Required] public string SlotId { get; set; } = null!;
    [Required] public string BatchId { get; set; } = null!;
    [Required] public string SubjectId { get; set; } = null!;
    public string? TeacherId { get; set; }
}

public sealed class CreateTimetableOverrideRequest
{
    [Required] public string OrganizationId { get; set; } = null!;
    [Required] public string CenterId { get; set; } = null!;
    [Required] public string RoomId { get; set; } = null!;
    public string? OriginalTimetableEntryId { get; set; }
    public DateTime? OriginalDate { get; set; }
    public string? OriginalSlotId { get; set; }
    [Required] public OverrideType OverrideType { get; set; }
    public string? NewBatchId { get; set; }
    public string? NewSubjectId { get; set; }
    public string? NewTeacherId { get; set; }
    public string? NewRoomId { get; set; }
    public DateTime? NewDate { get; set; }
    public TimeSpan? NewSlotStartTime { get; set; }
    public TimeSpan? NewSlotEndTime { get; set; }
    public string? NewSlotId { get; set; }
    [Required] public DateTime EffectiveDate { get; set; }
}
