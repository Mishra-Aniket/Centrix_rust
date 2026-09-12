using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using LectureAgent.Infrastructure.Database;
using LectureAgent.Infrastructure.Matching;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LectureAgent.Tests;

public class MatchingEngineTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LectureContext _dbContext;
    private readonly IConfiguration _config;

    public MatchingEngineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LectureContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new LectureContext(options);
        _dbContext.Database.EnsureCreated();

        var inMemorySettings = new Dictionary<string, string?>
        {
            {"Matching:HighConfidenceThreshold", "85"},
            {"Matching:MediumConfidenceThreshold", "60"},
            {"Matching:TimeOverlapMinimumPercentage", "50"},
            {"Matching:DurationToleranceMinutes", "10"}
        };

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private class TestTimetableProvider : ITimetableProvider
    {
        public List<TimetableEntry> Slots { get; set; } = new();
        public List<TimetableOverride> Overrides { get; set; } = new();

        public Task<List<TimetableEntry>> GetTimetableAsync(string centerId, string roomId, DateTime date) =>
            Task.FromResult(Slots);

        public Task<TimetableOverride?> GetOverrideAsync(string centerId, DateTime date, string slotId) =>
            Task.FromResult(Overrides.FirstOrDefault(o => o.OriginalSlotId == slotId));

        public Task<List<TimetableOverride>> GetActiveOverridesAsync(string centerId, DateTime date) =>
            Task.FromResult(Overrides);

        public Task SyncTimetableAsync(string centerId) => Task.CompletedTask;
    }

    [Fact]
    public async Task AnalyzeAsync_SkipsCancelledSlot_ReturnsNoMatch()
    {
        var date = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);
        var provider = new TestTimetableProvider
        {
            Slots = new List<TimetableEntry>
            {
                new()
                {
                    TimetableEntryId = "TTE-1",
                    SlotId = "SLOT-1",
                    CenterId = "C-1",
                    RoomId = "R-1",
                    ScheduledDate = date,
                    SlotStartTime = new TimeSpan(9, 0, 0),
                    SlotEndTime = new TimeSpan(10, 30, 0),
                    BatchId = "BATCH-A",
                    SubjectId = "PHYSICS"
                }
            },
            Overrides = new List<TimetableOverride>
            {
                new()
                {
                    OverrideId = "OVR-1",
                    CenterId = "C-1",
                    RoomId = "R-1",
                    OriginalSlotId = "SLOT-1",
                    OverrideType = OverrideType.Cancelled,
                    IsActive = true
                }
            }
        };

        var engine = new MatchingEngine(provider, _dbContext, _config, NullLogger<MatchingEngine>.Instance);

        var lecture = new LectureSession
        {
            LectureSessionId = "LSN-TEST-1",
            CenterId = "C-1",
            RoomId = "R-1",
            DetectedStartTime = date.AddHours(9).AddMinutes(5),
            DetectedEndTime = date.AddHours(10).AddMinutes(25),
            DetectedDurationSeconds = 80 * 60,
            VideoFileLocalPath = "/recordings/physics_class.mp4"
        };

        var result = await engine.AnalyzeAsync(lecture);

        Assert.Equal(MatchingDecision.NoMatch, result.Decision);
        Assert.Null(result.MatchedSlot);
    }

    [Fact]
    public async Task AnalyzeAsync_AppliesSlotOverrideValues()
    {
        var date = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);
        var provider = new TestTimetableProvider
        {
            Slots = new List<TimetableEntry>
            {
                new()
                {
                    TimetableEntryId = "TTE-2",
                    SlotId = "SLOT-2",
                    CenterId = "C-1",
                    RoomId = "R-1",
                    ScheduledDate = date,
                    SlotStartTime = new TimeSpan(10, 0, 0),
                    SlotEndTime = new TimeSpan(11, 30, 0),
                    BatchId = "BATCH-A",
                    SubjectId = "CHEMISTRY"
                }
            },
            Overrides = new List<TimetableOverride>
            {
                new()
                {
                    OverrideId = "OVR-2",
                    CenterId = "C-1",
                    RoomId = "R-1",
                    OriginalSlotId = "SLOT-2",
                    OverrideType = OverrideType.ChangedSubject,
                    NewSubjectId = "MATHEMATICS",
                    IsActive = true
                }
            }
        };

        var engine = new MatchingEngine(provider, _dbContext, _config, NullLogger<MatchingEngine>.Instance);

        var lecture = new LectureSession
        {
            LectureSessionId = "LSN-TEST-2",
            CenterId = "C-1",
            RoomId = "R-1",
            DetectedStartTime = date.AddHours(10).AddMinutes(2),
            DetectedEndTime = date.AddHours(11).AddMinutes(28),
            DetectedDurationSeconds = 86 * 60,
            VideoFileLocalPath = "/recordings/batch-a_mathematics.mp4"
        };

        var result = await engine.AnalyzeAsync(lecture);

        Assert.NotNull(result.MatchedSlot);
        Assert.Equal("MATHEMATICS", result.MatchedSlot.SubjectId);
        Assert.True(result.ConfidenceScore >= 85);
        Assert.Equal(MatchingDecision.AutoAssigned, result.Decision);
        Assert.Contains("Schedule override applied", result.ReasoningText);
    }
}
