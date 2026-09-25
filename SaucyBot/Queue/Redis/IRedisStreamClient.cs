namespace SaucyBot.Queue.Redis;

public interface IRedisStreamClient
{
    Task EnsureGroupAsync(CancellationToken cancellationToken);
    Task<string> AddAsync(string payload, CancellationToken cancellationToken);
    Task<RedisStreamEntry?> ReadNewAsync(string consumer, CancellationToken cancellationToken);
    Task<bool> RenewAsync(string consumer, string entryId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RedisStreamEntry>> ReclaimAsync(
        string consumer,
        TimeSpan minimumIdleTime,
        int count,
        CancellationToken cancellationToken);
    Task AcknowledgeAsync(string entryId, CancellationToken cancellationToken);
    Task DeleteAsync(string entryId, CancellationToken cancellationToken);
    Task ClearPendingAsync(CancellationToken cancellationToken);
}
