using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace SaucyBot.Queue.Redis;

public sealed class StackExchangeRedisStreamClient(
    IConnectionMultiplexer connection,
    RedisWorkQueueOptions options,
    ILogger<StackExchangeRedisStreamClient> logger) : IRedisStreamClient
{
    private readonly IDatabase _database = connection.GetDatabase();
    private readonly RedisWorkQueueOptions _options = options;
    private readonly ILogger<StackExchangeRedisStreamClient> _logger = logger;

    public async Task EnsureGroupAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // WaitAsync cancels this caller's wait; it cannot cancel an issued Redis command.
            await _database.StreamCreateConsumerGroupAsync(_options.StreamName, _options.ConsumerGroup, "0-0", createStream: true)
                .WaitAsync(cancellationToken);
        }
        catch (RedisServerException exception) when (exception.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    public async Task<string> AddAsync(string payload, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Do not issue a command after admission cancellation; WaitAsync is not command cancellation.
            var id = await _database.StreamAddAsync(_options.StreamName, RedisWorkQueue.PayloadField, payload)
                .WaitAsync(cancellationToken);
            return id.ToString();
        }
        catch
            (RedisServerException exception)
            when (
                exception.Message.Contains("MISCONF", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("OOM", StringComparison.OrdinalIgnoreCase)
            )
        {
            throw new RedisBackpressureException(exception.Message);
        }
        catch (RedisTimeoutException exception)
        {
            throw new RedisBackpressureException(exception.Message);
        }
        catch (RedisConnectionException exception)
        {
            throw new RedisBackpressureException(exception.Message);
        }
    }

    public async Task<RedisStreamEntry?> ReadNewAsync(
        string consumer,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = RedisWorkQueue.ConsumerName(consumer, leaseToken);
        var read = _database.StreamReadGroupAsync(_options.StreamName, _options.ConsumerGroup, owner, ">", count: 1);
        StreamEntry[] entries;
        try
        {
            entries = await read.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var grace = new CancellationTokenSource(_options.PendingReadTimeout);
            try
            {
                entries = await read.WaitAsync(grace.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or RedisException or TimeoutException)
            {
                _logger.LogWarning(
                    exception,
                    "Stream read for consumer {Consumer} did not finish within {PendingReadTimeout}.",
                    consumer,
                    _options.PendingReadTimeout);
                return null;
            }

            // The read may have claimed an entry remotely. Leave it pending so
            // recovery can reclaim it; cancellation must not acknowledge work.
            throw;
        }

        if (entries.Length == 0)
        {
            await Task.Delay(_options.RetryDelay, cancellationToken);
            return null;
        }

        return ToEntry(entries[0]);
    }

    public async Task<bool> RenewAsync(
        string consumer,
        string entryId,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _database.ScriptEvaluateAsync(
                RedisQueueScripts.RenewLease,
                [_options.StreamName],
                [_options.ConsumerGroup, entryId, consumer, leaseToken])
            .WaitAsync(cancellationToken);
        return (long)result == 1;
    }

    public async Task<RedisStreamEntry?> ReclaimAsync(
        string consumer,
        TimeSpan minimumIdleTime,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = RedisWorkQueue.ConsumerName(consumer, leaseToken);
        var reclaimed = await _database.StreamAutoClaimAsync(
                _options.StreamName,
                _options.ConsumerGroup,
                owner,
                Math.Max(0, (long)minimumIdleTime.TotalMilliseconds),
                "0-0",
                count: 1)
            .WaitAsync(cancellationToken);

        if (reclaimed.ClaimedEntries.Length == 0)
        {
            return null;
        }

        var entry = reclaimed.ClaimedEntries[0];
        var pending = await _database.StreamPendingMessagesAsync(
                _options.StreamName,
                _options.ConsumerGroup,
                1,
                owner,
                entry.Id,
                entry.Id)
            .WaitAsync(cancellationToken);

        var deliveryCount = pending.Length > 0
            ? pending[0].DeliveryCount
            : entry.DeliveryCount;
        return ToEntry(entry, deliveryCount);
    }

    public Task<LeaseOperationResult> CompleteAsync(
        string consumer,
        string entryId,
        string leaseToken,
        CancellationToken cancellationToken) =>
        ExecuteLeaseScriptAsync(
            RedisQueueScripts.CompleteLease,
            consumer,
            entryId,
            leaseToken,
            cancellationToken);

    public Task<LeaseOperationResult> RetryAsync(
        string consumer,
        string entryId,
        string leaseToken,
        CancellationToken cancellationToken) =>
        ExecuteLeaseScriptAsync(
            RedisQueueScripts.RetryLease,
            consumer,
            entryId,
            leaseToken,
            cancellationToken);

    private async Task<LeaseOperationResult> ExecuteLeaseScriptAsync(
        string script,
        string consumer,
        string entryId,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // All mutation scripts use only the stream key, so Redis Cluster
            // can route each script without cross-slot key requirements.
            var result = await _database.ScriptEvaluateAsync(
                script,
                [_options.StreamName],
                [_options.ConsumerGroup, entryId, consumer, leaseToken]);
            return (long)result switch
            {
                1 => LeaseOperationResult.Applied,
                2 => LeaseOperationResult.AlreadyApplied,
                -1 => LeaseOperationResult.LeaseLost,
                _ => LeaseOperationResult.OutcomeUnknown,
            };
        }
        catch (Exception exception) when (exception is RedisTimeoutException or RedisConnectionException)
        {
            _logger.LogWarning(
                exception,
                "Redis lease mutation outcome is unknown for queue entry {EntryId}",
                entryId);
            return LeaseOperationResult.OutcomeUnknown;
        }
    }

    public Task ClearPendingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _database.KeyDeleteAsync(_options.StreamName).WaitAsync(cancellationToken);
    }

    private static RedisStreamEntry ToEntry(StreamEntry entry, int? deliveryCount = null)
    {
        var payload = entry.Values.FirstOrDefault(x => x.Name == RedisWorkQueue.PayloadField).Value;
        return new RedisStreamEntry(
            entry.Id.ToString(),
            payload.ToString(),
            Math.Max(1, deliveryCount ?? entry.DeliveryCount));
    }
}
