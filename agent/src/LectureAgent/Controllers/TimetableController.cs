using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Infrastructure.Database;
using LectureAgent.Infrastructure.Timetable;
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
        [FromQuery] string? roomId,
        [FromQuery] DateTime? date)
    {
        var requestedDate = (date ?? DateTime.UtcNow).Date;
        var nextDate = requestedDate.AddDays(1);
        var query = _dbContext.TimetableEntries
            .AsNoTracking()
            .Where(entry => entry.CenterId == centerId
                && entry.ScheduledDate >= requestedDate
                && entry.ScheduledDate < nextDate);

        if (!string.IsNullOrWhiteSpace(roomId) && !string.Equals(roomId, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(entry => entry.RoomId == roomId);
        }

        var entries = await query.ToListAsync();
        return Ok(entries.OrderBy(entry => entry.SlotStartTime).ToList());
    }

    [HttpGet("dates")]
    public async Task<ActionResult<List<string>>> GetDates(
        [FromQuery, Required] string centerId,
        [FromQuery] string? roomId)
    {
        var query = _dbContext.TimetableEntries
            .AsNoTracking()
            .Where(entry => entry.CenterId == centerId);

        if (!string.IsNullOrWhiteSpace(roomId) && !string.Equals(roomId, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(entry => entry.RoomId == roomId);
        }

        var dates = await query
            .Select(entry => entry.ScheduledDate)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync();

        return Ok(dates.Select(d => d.ToString("yyyy-MM-dd")).ToList());
    }

    [HttpPost("sync")]
    public async Task<ActionResult> TriggerSync(
        [FromServices] GoogleSheetTimetableSyncService syncService,
        [FromQuery] string? centerId,
        [FromQuery] string? roomId = "ALL")
    {
        var count = await syncService.SyncScheduleAsync(_dbContext, centerId, roomId ?? "ALL");
        return Ok(new { count, message = $"Synced {count} timetable entries" });
    }

    [HttpGet("rooms")]
    public async Task<ActionResult<List<string>>> GetRooms([FromQuery, Required] string centerId)
    {
        var rooms = await _dbContext.TimetableEntries
            .AsNoTracking()
            .Where(entry => entry.CenterId == centerId && !string.IsNullOrEmpty(entry.RoomId))
            .Select(entry => entry.RoomId)
            .Distinct()
            .OrderBy(r => r)
            .ToListAsync();

        return Ok(rooms);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<TimetableSummaryDto>> GetSummary(
        [FromQuery, Required] string centerId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var now = DateTime.UtcNow;
        var diff = (int)now.DayOfWeek - (int)DayOfWeek.Monday;
        if (diff < 0) diff += 7;
        var monday = now.Date.AddDays(-diff);
        var start = (startDate ?? monday).Date;
        var end = (endDate ?? start.AddDays(7)).Date;

        var entries = await _dbContext.TimetableEntries
            .AsNoTracking()
            .Where(e => e.CenterId == centerId && e.ScheduledDate >= start && e.ScheduledDate < end)
            .Select(e => new { e.ScheduledDate, e.RoomId })
            .ToListAsync();

        var dayCounts = entries
            .GroupBy(e => e.ScheduledDate.Date.ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => g.Count());

        var roomCounts = entries
            .GroupBy(e => e.RoomId)
            .ToDictionary(g => g.Key, g => g.Count());

        return Ok(new TimetableSummaryDto
        {
            TotalLectures = entries.Count,
            TotalRooms = roomCounts.Keys.Count,
            DayCounts = dayCounts,
            RoomCounts = roomCounts
        });
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

public sealed class TimetableSummaryDto
{
    public int TotalLectures { get; set; }
    public int TotalRooms { get; set; }
    public Dictionary<string, int> DayCounts { get; set; } = new();
    public Dictionary<string, int> RoomCounts { get; set; } = new();
}
