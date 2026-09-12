using System.Collections.Concurrent;

namespace LectureAgent.Infrastructure.Cloud;

public sealed class UploadProgressStore
{
    private readonly ConcurrentDictionary<string, long> _bytesByQueueId = new();

    public void Set(string queueEntryId, long bytesUploaded) =>
        _bytesByQueueId[queueEntryId] = Math.Max(0, bytesUploaded);

    public bool TryGet(string queueEntryId, out long bytesUploaded) =>
        _bytesByQueueId.TryGetValue(queueEntryId, out bytesUploaded);

    public void Remove(string queueEntryId) =>
        _bytesByQueueId.TryRemove(queueEntryId, out _);
}
