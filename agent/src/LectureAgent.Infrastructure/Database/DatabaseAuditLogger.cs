using System.Text.Json;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Services;

namespace LectureAgent.Infrastructure.Database;

public sealed class DatabaseAuditLogger : IAuditLogger
{
    private readonly LectureContext _dbContext;

    public DatabaseAuditLogger(LectureContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LogAsync(
        string entityType,
        string entityId,
        string actionType,
        string? userId,
        object? oldValues,
        object? newValues,
        string? summary)
    {
        var audit = new AuditLogEntry
        {
            AuditEntryId = $"AUD-{Guid.NewGuid():N}",
            OrganizationId = "SYSTEM",
            CenterId = "SYSTEM",
            EntityType = entityType,
            EntityId = entityId,
            ActionType = actionType,
            UserId = userId,
            OldValues = Serialize(oldValues),
            NewValues = Serialize(newValues),
            ChangeSummary = summary,
            CreatedAt = DateTime.UtcNow
        };

        await _dbContext.AuditLogs.AddAsync(audit);
        await _dbContext.SaveChangesAsync();
    }

    private static string? Serialize(object? value) =>
        value == null ? null : JsonSerializer.Serialize(value);
}
