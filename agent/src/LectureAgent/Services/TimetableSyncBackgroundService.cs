using System;
using System.Threading;
using System.Threading.Tasks;
using LectureAgent.Infrastructure.Database;
using LectureAgent.Infrastructure.Timetable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LectureAgent.Services;

public sealed class TimetableSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TimetableSyncBackgroundService> _logger;
    private readonly AgentControlState _controlState;

    public TimetableSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<TimetableSyncBackgroundService> logger,
        AgentControlState controlState)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
        _controlState = controlState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = _config.GetValue<int>("GoogleSheet:SyncIntervalMinutes", 5);
        _logger.LogInformation("TimetableSyncBackgroundService initialized with interval: {IntervalMinutes}m", intervalMinutes);

        // Initial delay to let Kestrel and DB start
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_controlState.TimetableSyncPaused)
                {
                    _logger.LogDebug("Timetable sync is paused; skipping this cycle");
                }
                else
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<LectureContext>();
                    var syncService = scope.ServiceProvider.GetRequiredService<GoogleSheetTimetableSyncService>();

                    var centerId = _config["Agent:CenterId"];
                    var syncRoom = _config["GoogleSheet:SyncRoomId"] ?? "ALL";

                    _logger.LogInformation("Syncing live timetable from Google Sheet for Center {CenterId}, Room: {SyncRoom}...", centerId, syncRoom);
                    var count = await syncService.SyncScheduleAsync(dbContext, centerId, syncRoom, stoppingToken);
                    _logger.LogInformation("Live timetable sync complete: {Count} slots active for Center {CenterId} (Room: {SyncRoom})", count, centerId, syncRoom);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during background Google Sheet timetable sync");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("TimetableSyncBackgroundService stopped");
    }
}
