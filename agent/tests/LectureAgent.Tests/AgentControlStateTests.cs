using LectureAgent.Services;
using Xunit;

namespace LectureAgent.Tests;

public class AgentControlStateTests
{
    [Fact]
    public void Toggles_StartUnpaused_AndFlipWithTimestamps()
    {
        var state = new AgentControlState();

        Assert.False(state.UploadsPaused);
        Assert.False(state.MonitoringPaused);
        Assert.False(state.TimetableSyncPaused);

        state.SetUploadsPaused(true);
        state.SetMonitoringPaused(true);
        state.SetTimetableSyncPaused(true);

        Assert.True(state.UploadsPaused);
        Assert.NotNull(state.UploadsPausedAt);
        Assert.True(state.MonitoringPaused);
        Assert.NotNull(state.MonitoringPausedAt);
        Assert.True(state.TimetableSyncPaused);
        Assert.NotNull(state.TimetableSyncPausedAt);

        state.SetUploadsPaused(false);

        Assert.False(state.UploadsPaused);
        Assert.Null(state.UploadsPausedAt);
        // Other switches are untouched
        Assert.True(state.MonitoringPaused);
        Assert.True(state.TimetableSyncPaused);
    }
}
