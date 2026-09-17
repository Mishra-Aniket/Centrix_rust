namespace LectureAgent.Presentation.Controllers;

using LectureAgent.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;

/// <summary>Read-only audit history for operational and destructive actions.</summary>
[ApiController]
[Route("api/audit")]
public sealed class AuditController : ControllerBase
{
    private readonly LectureContext _context;

    public AuditController(LectureContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<object>> GetAudit(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        var (items, totalCount) = await new AuditLogRepository(_context).GetPagedAsync(entityType, entityId, offset, limit);
        return Ok(new
        {
            total = totalCount,
            count = items.Count,
            items = items.Select(item => new
            {
                item.AuditEntryId,
                item.EntityType,
                item.EntityId,
                item.ActionType,
                item.UserId,
                item.ChangeSummary,
                item.CreatedAt
            })
        });
    }
}
