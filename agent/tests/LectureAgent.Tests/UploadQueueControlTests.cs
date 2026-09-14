using LectureAgent.Application.Services;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using LectureAgent.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LectureAgent.Tests;

public class UploadQueueControlTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LectureContext _dbContext;
    private readonly UploadQueueService _queueService;

    public UploadQueueControlTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LectureContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new LectureContext(options);
        _dbContext.Database.EnsureCreated();

        var configuration = new ConfigurationBuilder().Build();
        _queueService = new UploadQueueService(
            new UploadQueueRepository(_dbContext),
            new NoopDriveUploader(),
            new LectureRepository(_dbContext),
            new DatabaseAuditLogger(_dbContext),
            configuration,
            NullLogger<UploadQueueService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private async Task<UploadQueueEntry> SeedEntryAsync(string id, UploadStatus status, int retryCount = 0)
    {
        var entry = new UploadQueueEntry
        {
            QueueEntryId = id,
            LectureSessionId = $"LSN-{id}",
            FileType = "VIDEO",
            LocalFilePath = $"/recordings/{id}.mp4",
            FileSizeBytes = 2048,
            Status = status,
            RetryCount = retryCount,
            NextRetryAt = status == UploadStatus.Failed ? DateTime.UtcNow.AddMinutes(5) : null,
            LastError = status == UploadStatus.Failed || status == UploadStatus.FailedPermanently ? "boom" : null,
            CreatedAt = DateTime.UtcNow
        };

        await _dbContext.UploadQueues.AddAsync(entry);
        await _dbContext.SaveChangesAsync();
        return entry;
    }

    [Fact]
    public async Task RetryAsync_ResetsPermanentlyFailedEntryToPending()
    {
        await SeedEntryAsync("UQ-DEAD", UploadStatus.FailedPermanently, retryCount: 5);

        var entry = await _queueService.RetryAsync("UQ-DEAD");

        Assert.Equal(UploadStatus.Pending, entry.Status);
        Assert.Equal(0, entry.RetryCount);
        Assert.Null(entry.NextRetryAt);
        Assert.Null(entry.LastError);

        var reloaded = await _dbContext.UploadQueues.SingleAsync(e => e.QueueEntryId == "UQ-DEAD");
        Assert.Equal(UploadStatus.Pending, reloaded.Status);
    }

    [Fact]
    public async Task RetryAsync_RejectsEntryThatIsCurrentlyUploading()
    {
        await SeedEntryAsync("UQ-BUSY", UploadStatus.Uploading);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _queueService.RetryAsync("UQ-BUSY"));
    }

    [Fact]
    public async Task CancelAsync_MarksPendingEntryCancelledAndExcludesItFromPendingQueries()
    {
        await SeedEntryAsync("UQ-GONE", UploadStatus.Pending);

        var entry = await _queueService.CancelAsync("UQ-GONE");

        Assert.Equal(UploadStatus.Cancelled, entry.Status);

        var repo = new UploadQueueRepository(_dbContext);
        var pending = await repo.GetPendingUploadsAsync(limit: 10);
        Assert.DoesNotContain(pending, e => e.QueueEntryId == "UQ-GONE");
    }

    [Fact]
    public async Task RetryAllFailedAsync_RequeuesEveryFailedEntry()
    {
        await SeedEntryAsync("UQ-F1", UploadStatus.Failed);
        await SeedEntryAsync("UQ-F2", UploadStatus.FailedPermanently);
        await SeedEntryAsync("UQ-OK", UploadStatus.Uploaded);

        var retried = await _queueService.RetryAllFailedAsync();

        Assert.Equal(2, retried);

        var reloadedF1 = await _dbContext.UploadQueues.SingleAsync(e => e.QueueEntryId == "UQ-F1");
        var reloadedF2 = await _dbContext.UploadQueues.SingleAsync(e => e.QueueEntryId == "UQ-F2");
        Assert.Equal(UploadStatus.Pending, reloadedF1.Status);
        Assert.Equal(UploadStatus.Pending, reloadedF2.Status);
    }

    [Fact]
    public async Task GetRetryableUploadsAsync_ReturnsOnlyFailedAndPermanentEntries()
    {
        await SeedEntryAsync("UQ-F1", UploadStatus.Failed);
        await SeedEntryAsync("UQ-F2", UploadStatus.FailedPermanently);
        await SeedEntryAsync("UQ-P", UploadStatus.Pending);

        var repo = new UploadQueueRepository(_dbContext);
        var retryable = await repo.GetRetryableUploadsAsync();

        Assert.Equal(2, retryable.Count);
        Assert.DoesNotContain(retryable, e => e.QueueEntryId == "UQ-P");
    }

    [Fact]
    public async Task EnqueueFileAsync_RejectsSameContentUnderDifferentFileName()
    {
        var first = await _queueService.EnqueueFileAsync("LSN-A", "VIDEO", "/rec/class_a.mkv", 1000, "hash-123");
        Assert.NotNull(first);

        // Same content saved under a different name/session - must be rejected (null)
        var second = await _queueService.EnqueueFileAsync("LSN-B", "VIDEO", "/rec/copy_of_class_a.mp4", 1000, "hash-123");
        Assert.Null(second);

        // Different content still goes through
        var third = await _queueService.EnqueueFileAsync("LSN-C", "VIDEO", "/rec/class_c.mkv", 2000, "hash-456");
        Assert.NotNull(third);
    }

    [Fact]
    public async Task EnqueueFileAsync_AllowsRetryWhenPreviousUploadWasCancelled()
    {
        var cancelledEntry = await SeedEntryAsync("UQ-C", UploadStatus.Cancelled);
        cancelledEntry.FileHash = "hash-dup";
        await _dbContext.SaveChangesAsync();

        // A cancelled entry must not block a fresh upload of the same content
        var retried = await _queueService.EnqueueFileAsync("LSN-D", "VIDEO", "/rec/same.mkv", 1000, "hash-dup");
        Assert.NotNull(retried);
    }

    private sealed class NoopDriveUploader : IGoogleDriveUploader
    {
        public Task<string> AuthorizeAsync() => Task.FromResult("test_token");
        public Task<string> UploadFileAsync(UploadQueueEntry entry, System.Threading.CancellationToken ct = default) => Task.FromResult("drive_id");
        public Task<bool> VerifyUploadAsync(string fileId, string expectedHash) => Task.FromResult(true);
        public Task CreateFolderStructureAsync(string folderPath) => Task.CompletedTask;
        public Task<List<string>> ListFoldersAsync(string? query = null, int limit = 200, string? underPath = null, System.Threading.CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<string>());
    }
}
