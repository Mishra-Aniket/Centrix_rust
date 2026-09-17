namespace LectureAgent.Infrastructure.Database;

using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Generic repository implementation for SQLite.
/// </summary>
public class GenericRepository<T> : IRepository<T> where T : class
{
    protected readonly LectureContext _context;
    protected readonly DbSet<T> _dbSet;

    public GenericRepository(LectureContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(string id)
    {
        return await _dbSet.FindAsync(id);
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        return await _dbSet.ToListAsync();
    }

    public virtual async Task AddAsync(T entity)
    {
        await _dbSet.AddAsync(entity);
    }

    public virtual async Task UpdateAsync(T entity)
    {
        _dbSet.Update(entity);
        await Task.CompletedTask;
    }

    public virtual async Task DeleteAsync(string id)
    {
        var entity = await GetByIdAsync(id);
        if (entity != null)
        {
            _dbSet.Remove(entity);
        }
    }

    public virtual async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}

/// <summary>
/// Repository for lectures.
/// </summary>
public class LectureRepository : GenericRepository<LectureSession>, ILectureRepository
{
    public LectureRepository(LectureContext context) : base(context) { }

    public async Task<List<LectureSession>> GetByCenterAndDateAsync(string centerId, DateTime date)
    {
        return await _dbSet
            .Where(l => l.CenterId == centerId && l.DetectedStartTime.Date == date.Date)
            .OrderBy(l => l.DetectedStartTime)
            .ToListAsync();
    }

    public async Task<List<LectureSession>> GetByStatusAsync(LectureStatus status)
    {
        return await _dbSet
            .Where(l => l.Status == status)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<LectureSession>> GetPendingReviewAsync(string centerId)
    {
        return await _dbSet
            .Where(l => l.CenterId == centerId
                && l.Status != LectureStatus.Uploaded
                && l.Status != LectureStatus.Verified
                && l.Status != LectureStatus.Cancelled
                && l.Status != LectureStatus.Rejected
                && l.Status != LectureStatus.Archived)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task<LectureSession?> GetByVideoLocalPathAsync(string videoLocalPath)
    {
        return await _dbSet
            .FirstOrDefaultAsync(l => l.VideoFileLocalPath == videoLocalPath);
    }

    public async Task<LectureSession?> GetByFilePathAsync(string filePath)
    {
        return await _dbSet
            .FirstOrDefaultAsync(l => l.VideoFileLocalPath == filePath || l.PdfFileLocalPath == filePath);
    }

    public async Task<(List<LectureSession> Items, int TotalCount)> GetPagedLecturesAsync(string? centerId, LectureStatus? status, int offset, int limit)
    {
        var query = _dbSet.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(centerId))
            query = query.Where(l => l.CenterId == centerId);

        if (status.HasValue)
            query = query.Where(l => l.Status == status.Value);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<LectureSummary> GetSummaryAsync(string centerId)
    {
        var query = _dbSet.Where(l => l.CenterId == centerId);

        return new LectureSummary
        {
            Total = await query.CountAsync(),
            Uploaded = await query.CountAsync(l => l.Status == LectureStatus.Uploaded || l.Status == LectureStatus.Verified),
            Matched = await query.CountAsync(l => l.MatchStatus == MatchStatus.Matched),
            Unmatched = await query.CountAsync(l => l.MatchStatus == MatchStatus.NoMatch),
            FailedUpload = await query.CountAsync(l => l.Status == LectureStatus.UploadFailed),
            PendingReview = await query.CountAsync(l =>
                l.Status == LectureStatus.ReviewRequired || l.ReviewStatus == ReviewStatus.Pending)
        };
    }

    public async Task<List<LectureSession>> GetByYouTubePublishStatusAsync(params YouTubePublishStatus[] statuses)
    {
        if (statuses.Length == 0)
            return new List<LectureSession>();

        return await _dbSet
            .Where(l => statuses.Contains(l.YouTubePublishStatus))
            .OrderBy(l => l.UpdatedAt)
            .ToListAsync();
    }
}

/// <summary>
/// Repository for upload queue entries.
/// </summary>
public class UploadQueueRepository : GenericRepository<UploadQueueEntry>, IUploadQueueRepository
{
    public UploadQueueRepository(LectureContext context) : base(context) { }

    public async Task<List<UploadQueueEntry>> GetPendingUploadsAsync(int limit)
    {
        // Only pick up entries that are Pending, Failed, or Uploading entries that have
        // been stuck for >1 hour (stale — the original uploader likely crashed).
        // This prevents two threads from uploading the same file simultaneously.
        var staleThreshold = DateTime.UtcNow.AddHours(-1);
        return await _dbSet
            .AsNoTracking()
            .Where(e => e.Status == UploadStatus.Pending
                || e.Status == UploadStatus.Failed
                || (e.Status == UploadStatus.Uploading && e.UpdatedAt < staleThreshold))
            .OrderBy(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<UploadQueueEntry>> GetRetryableUploadsAsync()
    {
        return await _dbSet
            .Where(e => e.Status == UploadStatus.Failed
                || e.Status == UploadStatus.FailedPermanently)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();
    }

    public async Task<UploadQueueEntry?> GetActiveByFileHashAsync(string fileHash)
    {
        if (string.IsNullOrWhiteSpace(fileHash))
            return null;

        return await _dbSet
            .Where(e => e.FileHash == fileHash
                && (e.Status == UploadStatus.Pending
                    || e.Status == UploadStatus.Uploading
                    || e.Status == UploadStatus.Uploaded))
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<List<UploadQueueEntry>> GetByLectureSessionIdAsync(string lectureSessionId)
    {
        return await _dbSet
            .Where(e => e.LectureSessionId == lectureSessionId)
            .ToListAsync();
    }
}

/// <summary>
/// Repository for timetable entries.
/// </summary>
public class TimetableRepository : GenericRepository<TimetableEntry>
{
    public TimetableRepository(LectureContext context) : base(context) { }

    public async Task<List<TimetableEntry>> GetByCenterAndDateAsync(string centerId, DateTime date)
    {
        return await _dbSet
            .Where(t => t.CenterId == centerId && t.ScheduledDate == date.Date)
            .OrderBy(t => t.SlotStartTime)
            .ToListAsync();
    }

    public async Task<List<TimetableEntry>> GetByRoomAndDateAsync(string roomId, DateTime date)
    {
        return await _dbSet
            .Where(t => t.RoomId == roomId && t.ScheduledDate == date.Date)
            .OrderBy(t => t.SlotStartTime)
            .ToListAsync();
    }
}

/// <summary>
/// Repository for audit logs.
/// </summary>
public class AuditLogRepository : GenericRepository<AuditLogEntry>
{
    public AuditLogRepository(LectureContext context) : base(context) { }

    public async Task<List<AuditLogEntry>> GetByEntityAsync(string entityType, string entityId)
    {
        return await _dbSet
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<AuditLogEntry>> GetUnsyncedAsync()
    {
        return await _dbSet
            .Where(a => !a.SyncedToCloud)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<(List<AuditLogEntry> Items, int TotalCount)> GetPagedAsync(
        string? entityType,
        string? entityId,
        int offset,
        int limit)
    {
        var query = _dbSet.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(entityId))
            query = query.Where(a => a.EntityId == entityId);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip(Math.Max(0, offset))
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync();

        return (items, totalCount);
    }
}

/// <summary>
/// Repository for configuration.
/// </summary>
public class ConfigRepository : GenericRepository<ConfigEntry>
{
    public ConfigRepository(LectureContext context) : base(context) { }

    public async Task<string?> GetValueAsync(string key)
    {
        var config = await _dbSet.FirstOrDefaultAsync(c => c.ConfigKey == key);
        return config?.ConfigValue;
    }

    public async Task<int> GetIntValueAsync(string key, int defaultValue = 0)
    {
        var value = await GetValueAsync(key);
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    public async Task<bool> GetBoolValueAsync(string key, bool defaultValue = false)
    {
        var value = await GetValueAsync(key);
        return bool.TryParse(value, out var result) ? result : defaultValue;
    }

    public async Task SetValueAsync(string key, string value)
    {
        var config = await _dbSet.FirstOrDefaultAsync(c => c.ConfigKey == key);
        if (config == null)
        {
            config = new ConfigEntry { ConfigKey = key, ConfigValue = value, DataType = "STRING" };
            await AddAsync(config);
        }
        else
        {
            config.ConfigValue = value;
            config.LastUpdated = DateTime.UtcNow;
            await UpdateAsync(config);
        }
        await SaveChangesAsync();
    }
}
