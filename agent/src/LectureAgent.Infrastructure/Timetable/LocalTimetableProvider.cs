using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Services;
using LectureAgent.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LectureAgent.Infrastructure.Timetable;

public sealed class LocalTimetableProvider : ITimetableProvider
{
    private readonly LectureContext _dbContext;
    private readonly GoogleSheetTimetableSyncService? _sheetSyncService;

    public LocalTimetableProvider(LectureContext dbContext, GoogleSheetTimetableSyncService? sheetSyncService = null)
    {
        _dbContext = dbContext;
        _sheetSyncService = sheetSyncService;
    }

    public async Task<List<TimetableEntry>> GetTimetableAsync(string centerId, string roomId, DateTime date)
    {
        var start = date.Date;
        var end = start.AddDays(1);

        var entries = await _dbContext.TimetableEntries
            .AsNoTracking()
            .Where(entry => entry.CenterId == centerId
                && entry.RoomId == roomId
                && entry.ScheduledDate >= start
                && entry.ScheduledDate < end)
            .ToListAsync();

        return entries.OrderBy(entry => entry.SlotStartTime).ToList();
    }

    public Task<TimetableOverride?> GetOverrideAsync(string centerId, DateTime date, string slotId)
    {
        var start = date.Date;
        var end = start.AddDays(1);

        return _dbContext.TimetableOverrides
            .AsNoTracking()
            .Where(item => item.CenterId == centerId
                && item.OriginalSlotId == slotId
                && item.EffectiveDate >= start
                && item.EffectiveDate < end
                && item.IsActive)
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync();
    }

    public Task<List<TimetableOverride>> GetActiveOverridesAsync(string centerId, DateTime date)
    {
        var start = date.Date;
        var end = start.AddDays(1);

        return _dbContext.TimetableOverrides
            .AsNoTracking()
            .Where(item => item.CenterId == centerId
                && item.EffectiveDate >= start
                && item.EffectiveDate < end
                && item.IsActive)
            .OrderBy(item => item.EffectiveDate)
            .ToListAsync();
    }

    public async Task SyncTimetableAsync(string centerId)
    {
        if (_sheetSyncService != null)
        {
            await _sheetSyncService.SyncScheduleAsync(_dbContext, centerId);
        }
    }
}
