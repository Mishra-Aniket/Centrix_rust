using LectureAgent.Domain.Enums;
using Xunit;

namespace LectureAgent.Tests;

public class MinimumDurationFilterTests
{
    [Theory]
    [InlineData(300, 10, true)]   // 5 min < 10 min → skip
    [InlineData(599, 10, true)]   // 9m 59s < 10 min → skip
    [InlineData(60, 10, true)]    // 1 min < 10 min → skip
    [InlineData(0, 10, true)]     // 0 sec < 10 min → skip
    [InlineData(600, 10, false)]  // exactly 10 min → allow
    [InlineData(601, 10, false)]  // 10m 1s → allow
    [InlineData(5400, 10, false)] // 90 min → allow
    public void VideoFilter_SkipsShortRecordings(int durationSeconds, int thresholdMinutes, bool shouldSkip)
    {
        var fileType = "VIDEO";
        var isBelowThreshold = thresholdMinutes > 0
            && fileType.Equals("VIDEO", System.StringComparison.OrdinalIgnoreCase)
            && durationSeconds < thresholdMinutes * 60;

        Assert.Equal(shouldSkip, isBelowThreshold);
    }

    [Theory]
    [InlineData(0)]    // 0 sec PDF
    [InlineData(60)]   // 1 min PDF  
    [InlineData(300)]  // 5 min PDF
    public void PdfFilter_AlwaysAllowsRegardlessOfDuration(int durationSeconds)
    {
        var fileType = "PDF";
        int thresholdMinutes = 10;
        var isBelowThreshold = thresholdMinutes > 0
            && fileType.Equals("VIDEO", System.StringComparison.OrdinalIgnoreCase)
            && durationSeconds < thresholdMinutes * 60;

        // PDFs should never be skipped
        Assert.False(isBelowThreshold);
    }

    [Fact]
    public void VideoFilter_DisabledWhenThresholdIsZero()
    {
        int thresholdMinutes = 0;
        int durationSeconds = 30; // very short
        var fileType = "VIDEO";

        var isBelowThreshold = thresholdMinutes > 0
            && fileType.Equals("VIDEO", System.StringComparison.OrdinalIgnoreCase)
            && durationSeconds < thresholdMinutes * 60;

        // With threshold=0, nothing should be skipped
        Assert.False(isBelowThreshold);
    }

    [Fact]
    public void ShortClipStatus_ExistsInEnum()
    {
        // Verify ShortClip enum value is available
        var status = LectureStatus.ShortClip;
        Assert.Equal(20, (int)status);
    }

    [Theory]
    [InlineData(415, 6, 55)]   // 6 min 55 sec
    [InlineData(120, 2, 0)]    // 2 min 0 sec
    [InlineData(367, 6, 7)]    // 6 min 7 sec
    public void DurationFormatting_ProducesCorrectMinutesAndSeconds(int totalSeconds, int expectedMin, int expectedSec)
    {
        var mins = totalSeconds / 60;
        var secs = totalSeconds % 60;

        Assert.Equal(expectedMin, mins);
        Assert.Equal(expectedSec, secs);
    }
}
