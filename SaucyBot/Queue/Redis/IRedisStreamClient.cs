namespace SaucyBot.Queue.Redis;

public interface IRedisStreamClient
{
    Task EnsureGroupAsync(CancellationToken cancellationToken);
    Task<string> AddAsync(string payload, CancellationToken cancellationToken);
    Task<RedisStreamEntry?> ReadNewAsync(string consumer, string leaseToken, CancellationToken cancellationToken);
    Task<bool> RenewAsync(string consumer, string entryId, string leaseToken, CancellationToken cancellationToken);
    Task<RedisStreamEntry?> ReclaimAsync(
        string consumer,
        TimeSpan minimumIdleTime,
        string leaseToken,
        CancellationToken cancellationToken);
    Task<LeaseOperationResult> CompleteAsync(
        string consumer,
        string entryId,
        string leaseToken,
        CancellationToken cancellationToken);
    Task<LeaseOperationResult> RetryAsync(
        string consumer,
        string entryId,
        string leaseToken,
        CancellationToken cancellationToken);
    Task ClearPendingAsync(CancellationToken cancellationToken);
}
