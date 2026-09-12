using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Services;
using LectureAgent.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LectureAgent.Services;

public sealed class CloudSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CloudSyncBackgroundService> _logger;

    public CloudSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<CloudSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue("CloudSync:Enabled", false);
        var intervalSeconds = _configuration.GetValue("CloudSync:IntervalSeconds", 60);

        if (!enabled)
        {
            _logger.LogInformation("Cloud metadata sync is disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncPendingAuditsAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }

    private async Task SyncPendingAuditsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LectureContext>();
        var cloudSync = scope.ServiceProvider.GetRequiredService<ICloudMetadataSync>();
        var pending = await dbContext.AuditLogs
            .Where(audit => !audit.SyncedToCloud)
            .OrderBy(audit => audit.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var audit in pending)
        {
            if (!await cloudSync.SyncAuditAsync(audit, cancellationToken))
                continue;

            audit.SyncedToCloud = true;
            audit.CloudSyncAt = DateTime.UtcNow;
        }

        if (pending.Any(audit => audit.SyncedToCloud))
            await dbContext.SaveChangesAsync(cancellationToken);
    }
}
