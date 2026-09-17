using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LectureAgent.Tests;

public class LectureSummaryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LectureContext _dbContext;

    public LectureSummaryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _dbContext = new LectureContext(new DbContextOptionsBuilder<LectureContext>()
            .UseSqlite(_connection)
            .Options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsCenterScopedPipelineCounts()
    {
        _dbContext.LectureSessions.AddRange(
            Lecture("1", "CENTER-A", LectureStatus.Uploaded, MatchStatus.Matched, ReviewStatus.Approved),
            Lecture("2", "CENTER-A", LectureStatus.Verified, MatchStatus.Matched, ReviewStatus.Approved),
            Lecture("3", "CENTER-A", LectureStatus.ReviewRequired, MatchStatus.NoMatch, ReviewStatus.Pending),
            Lecture("4", "CENTER-A", LectureStatus.UploadFailed, MatchStatus.Pending, ReviewStatus.Approved),
            Lecture("5", "CENTER-A", LectureStatus.Confirmed, MatchStatus.Pending, ReviewStatus.Pending),
            Lecture("6", "CENTER-B", LectureStatus.Uploaded, MatchStatus.Matched, ReviewStatus.Approved));
        await _dbContext.SaveChangesAsync();

        var summary = await new LectureRepository(_dbContext).GetSummaryAsync("CENTER-A");

        Assert.Equal(5, summary.Total);
        Assert.Equal(2, summary.Uploaded);
        Assert.Equal(2, summary.Matched);
        Assert.Equal(1, summary.Unmatched);
        Assert.Equal(1, summary.FailedUpload);
        Assert.Equal(2, summary.PendingReview);
    }

    private static LectureSession Lecture(
        string id,
        string centerId,
        LectureStatus status,
        MatchStatus matchStatus,
        ReviewStatus reviewStatus) => new()
    {
        LectureSessionId = $"LSN-{id}",
        OrganizationId = "ORG",
        CenterId = centerId,
        RoomId = "603",
        DeviceId = "DEVICE",
        DetectedStartTime = DateTime.UtcNow.AddHours(-2),
        DetectedEndTime = DateTime.UtcNow.AddHours(-1),
        DetectedDurationSeconds = 3600,
        Status = status,
        MatchStatus = matchStatus,
        ReviewStatus = reviewStatus
    };
}
