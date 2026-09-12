using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using Xunit;

namespace LectureAgent.Tests;

public class LectureSessionTests
{
    [Fact]
    public void GetDurationMinutes_ReturnsDetectedDurationInMinutes()
    {
        var session = new LectureSession { DetectedDurationSeconds = 5400 };

        Assert.Equal(90, session.GetDurationMinutes());
    }

    [Fact]
    public void GetTimeOverlapPercentage_ReturnsOverlapAgainstScheduledDuration()
    {
        var session = new LectureSession
        {
            DetectedStartTime = new DateTime(2026, 9, 2, 9, 10, 0, DateTimeKind.Utc),
            DetectedEndTime = new DateTime(2026, 9, 2, 10, 10, 0, DateTimeKind.Utc),
            ScheduledStartTime = new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc),
            ScheduledEndTime = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc)
        };

        Assert.Equal(83.33333333333333, session.GetTimeOverlapPercentage(), precision: 10);
    }

    [Fact]
    public void IsComplete_IsTrueOnlyWhenVerified()
    {
        var session = new LectureSession { Status = LectureStatus.Verified };

        Assert.True(session.IsComplete());
    }
}
