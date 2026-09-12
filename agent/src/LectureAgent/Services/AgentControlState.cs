namespace LectureAgent.Services;

/// <summary>
/// Process-wide runtime switches that the dashboard can toggle while the agent is running.
/// Deliberately not persisted: pausing is a temporary operational action, and a restart
/// should always return the agent to fully automatic behaviour.
/// </summary>
public sealed class AgentControlState
{
    private readonly object _lock = new();

    public bool UploadsPaused { get; private set; }
    public DateTime? UploadsPausedAt { get; private set; }

    public bool MonitoringPaused { get; private set; }
    public DateTime? MonitoringPausedAt { get; private set; }

    public bool TimetableSyncPaused { get; private set; }
    public DateTime? TimetableSyncPausedAt { get; private set; }

    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

    public void SetUploadsPaused(bool paused)
    {
        lock (_lock)
        {
            UploadsPaused = paused;
            UploadsPausedAt = paused ? DateTime.UtcNow : null;
        }
    }

    public void SetMonitoringPaused(bool paused)
    {
        lock (_lock)
        {
            MonitoringPaused = paused;
            MonitoringPausedAt = paused ? DateTime.UtcNow : null;
        }
    }

    public void SetTimetableSyncPaused(bool paused)
    {
        lock (_lock)
        {
            TimetableSyncPaused = paused;
            TimetableSyncPausedAt = paused ? DateTime.UtcNow : null;
        }
    }
}
