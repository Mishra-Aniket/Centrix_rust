namespace LectureAgent.Infrastructure.YouTube;

using System.Threading.Channels;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public enum YouTubePublishJobKind { Publish, Unpublish }

public sealed record YouTubePublishJob(string LectureSessionId, YouTubePublishJobKind Kind);

/// <summary>In-process queue with database-backed request state for recovery after restart.</summary>
public sealed class YouTubePublishQueue
{
    private readonly Channel<YouTubePublishJob> _jobs = Channel.CreateUnbounded<YouTubePublishJob>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public bool TryEnqueue(YouTubePublishJob job) => _jobs.Writer.TryWrite(job);
    public IAsyncEnumerable<YouTubePublishJob> ReadAllAsync(CancellationToken cancellationToken) => _jobs.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Processes YouTube actions off the request thread and limits daily API operations.
/// Queued work is restored from the database whenever the agent starts.
/// </summary>
public sealed class YouTubeBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly YouTubePublishQueue _queue;
    private readonly IConfiguration _configuration;
    private readonly ILogger<YouTubeBackgroundWorker> _logger;
    private readonly object _quotaLock = new();
    private DateOnly _quotaDay = DateOnly.FromDateTime(DateTime.UtcNow);
    private int _operationsToday;

    public YouTubeBackgroundWorker(
        IServiceScopeFactory scopeFactory,
        YouTubePublishQueue queue,
        IConfiguration configuration,
        ILogger<YouTubeBackgroundWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RestoreQueuedWorkAsync(stoppingToken);
        await foreach (var job in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled YouTube queue error for {LectureId}", job.LectureSessionId);
            }
        }
    }

    private async Task RestoreQueuedWorkAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ILectureRepository>();
        var queued = await repository.GetByYouTubePublishStatusAsync(
            YouTubePublishStatus.PublishQueued,
            YouTubePublishStatus.UnpublishQueued);
        foreach (var lecture in queued)
        {
            _queue.TryEnqueue(new YouTubePublishJob(
                lecture.LectureSessionId,
                lecture.YouTubePublishStatus == YouTubePublishStatus.UnpublishQueued
                    ? YouTubePublishJobKind.Unpublish
                    : YouTubePublishJobKind.Publish));
        }
        if (queued.Count > 0)
            _logger.LogInformation("Restored {Count} queued YouTube action(s)", queued.Count);
    }

    private async Task ProcessAsync(YouTubePublishJob job, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ILectureRepository>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
        var lecture = await repository.GetByIdAsync(job.LectureSessionId);
        if (lecture == null)
            return;

        if (!IsEnabled())
        {
            await MarkFailedAsync(repository, lecture, "YouTube publishing is disabled in the agent configuration.", cancellationToken);
            return;
        }
        if (!TryConsumeQuota())
        {
            await MarkFailedAsync(repository, lecture, "Daily YouTube operation limit reached. Retry after UTC midnight.", cancellationToken);
            return;
        }

        try
        {
            lecture.YouTubePublishStatus = job.Kind == YouTubePublishJobKind.Publish
                ? YouTubePublishStatus.Publishing
                : YouTubePublishStatus.Unpublishing;
            lecture.YouTubeFailureReason = null;
            lecture.UpdatedAt = DateTime.UtcNow;
            await repository.UpdateAsync(lecture);
            await repository.SaveChangesAsync();

            var publisher = scope.ServiceProvider.GetRequiredService<IYouTubePublisher>();
            if (job.Kind == YouTubePublishJobKind.Publish)
            {
                var result = await publisher.PublishAsync(lecture, cancellationToken);
                lecture.YouTubeId = result.VideoId;
                lecture.YouTubeThumbnailUrl = result.ThumbnailUrl;
                lecture.YouTubePublishStatus = YouTubePublishStatus.Published;
                lecture.YouTubePublishedAt = DateTime.UtcNow;
                await auditLogger.LogAsync("LECTURE_SESSION", lecture.LectureSessionId, "YOUTUBE_PUBLISHED", null,
                    null, new { result.VideoId }, "Lecture published to YouTube as unlisted");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(lecture.YouTubeId))
                    throw new InvalidOperationException("This lecture has no YouTube video to unpublish.");
                await publisher.UnpublishAsync(lecture.YouTubeId, cancellationToken);
                lecture.YouTubePublishStatus = YouTubePublishStatus.Unpublished;
                await auditLogger.LogAsync("LECTURE_SESSION", lecture.LectureSessionId, "YOUTUBE_UNPUBLISHED", null,
                    null, new { lecture.YouTubeId }, "YouTube video made private");
            }

            lecture.UpdatedAt = DateTime.UtcNow;
            await repository.UpdateAsync(lecture);
            await repository.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YouTube {Action} failed for {LectureId}", job.Kind, lecture.LectureSessionId);
            await MarkFailedAsync(repository, lecture, ex.Message, cancellationToken);
        }
    }

    private bool IsEnabled() => _configuration.GetValue("YouTube:Enabled", false);

    private bool TryConsumeQuota()
    {
        lock (_quotaLock)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today != _quotaDay)
            {
                _quotaDay = today;
                _operationsToday = 0;
            }
            var maxPerDay = Math.Max(1, _configuration.GetValue("YouTube:MaxDailyOperations", 6));
            if (_operationsToday >= maxPerDay)
                return false;
            _operationsToday++;
            return true;
        }
    }

    private static async Task MarkFailedAsync(
        ILectureRepository repository,
        LectureSession lecture,
        string reason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lecture.YouTubePublishStatus = YouTubePublishStatus.Failed;
        lecture.YouTubeFailureReason = reason;
        lecture.UpdatedAt = DateTime.UtcNow;
        await repository.UpdateAsync(lecture);
        await repository.SaveChangesAsync();
    }
}
