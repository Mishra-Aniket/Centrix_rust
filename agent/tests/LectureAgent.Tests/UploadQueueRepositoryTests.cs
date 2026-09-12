using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LectureAgent.Tests;

public class UploadQueueRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LectureContext _dbContext;

    public UploadQueueRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LectureContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new LectureContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetPendingUploadsAsync_RespectsLimitAndOrdersByCreatedAt()
    {
        var repo = new UploadQueueRepository(_dbContext);

        for (int i = 1; i <= 5; i++)
        {
            await repo.AddAsync(new UploadQueueEntry
            {
                QueueEntryId = $"UQ-{i}",
                LectureSessionId = $"LSN-{i}",
                FileType = "VIDEO",
                LocalFilePath = $"/recordings/video_{i}.mp4",
                FileSizeBytes = 1000 * i,
                Status = UploadStatus.Pending,
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }

        // Add an already uploaded entry that shouldn't be picked up
        await repo.AddAsync(new UploadQueueEntry
        {
            QueueEntryId = "UQ-DONE",
            LectureSessionId = "LSN-DONE",
            FileType = "VIDEO",
            LocalFilePath = "/recordings/video_done.mp4",
            FileSizeBytes = 5000,
            Status = UploadStatus.Uploaded,
            CreatedAt = DateTime.UtcNow
        });

        await repo.SaveChangesAsync();

        var pending = await repo.GetPendingUploadsAsync(limit: 3);

        Assert.Equal(3, pending.Count);
        Assert.Equal("UQ-1", pending[0].QueueEntryId);
        Assert.Equal("UQ-2", pending[1].QueueEntryId);
        Assert.Equal("UQ-3", pending[2].QueueEntryId);
    }
}
