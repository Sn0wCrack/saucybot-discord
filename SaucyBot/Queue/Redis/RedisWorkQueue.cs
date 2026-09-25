using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Diagnostics;

namespace SaucyBot.Queue.Redis;

public sealed class RedisWorkQueue : IMessageWorkQueue, IWorkItemProducer<MessageWorkItem>, IWorkItemConsumer<MessageWorkItem>
{
    internal const string PayloadField = "payload";

    private sealed record PreparedDelivery(
        string EntryId,
        MessageWorkItem Item,
        int Attempt,
        DateTimeOffset ReceivedAt,
        string LeaseToken);

    private readonly IRedisStreamClient _client;
    private readonly WorkQueueOptions _options;
    private readonly RedisWorkQueueOptions _redisOptions;
    private readonly ISaucyBotMetrics? _metrics;
    private readonly ILogger<RedisWorkQueue> _logger;

    public RedisWorkQueue(
        IRedisStreamClient client,
        WorkQueueOptions options,
        RedisWorkQueueOptions redisOptions,
        ISaucyBotMetrics? metrics = null,
        ILogger<RedisWorkQueue>? logger = null)
    {
        _client = client;
        _options = options;
        _redisOptions = redisOptions;
        _metrics = metrics;
        _logger = logger ?? NullLogger<RedisWorkQueue>.Instance;
    }

    public async Task<EnqueueResult> EnqueueAsync(
        MessageWorkItem item,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = timeout == Timeout.InfiniteTimeSpan
            ? (DateTimeOffset?)null
            : DateTimeOffset.UtcNow + timeout;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await _client.AddAsync(item.Serialize(), cancellationToken);
                    _metrics?.Enqueued.Add(1);
                    _metrics?.QueueDepth.Add(1);
                    return EnqueueResult.Accepted;
                }
                catch (RedisBackpressureException)
                {
                    _metrics?.Retried.Add(1);
                    var delay = _redisOptions.RetryDelay;
                    if (deadline is not null)
                    {
                        var remaining = deadline.Value - DateTimeOffset.UtcNow;
                        if (remaining <= TimeSpan.Zero)
                        {
                            _logger.LogWarning("Redis queue stayed unavailable for the whole {Timeout} enqueue limit", timeout);
                            return EnqueueResult.TimedOut;
                        }

                        if (delay > remaining)
                        {
                            delay = remaining;
                        }
                    }

                    _logger.LogDebug(
                        "Redis queue is applying backpressure; retrying enqueue after {RetryDelay}",
                        _redisOptions.RetryDelay);
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _metrics?.Cancelled.Add(1);
            throw;
        }
    }

    public Task EnqueueAsync(MessageWorkItem item, CancellationToken cancellationToken) =>
        EnqueueAsync(item, Timeout.InfiniteTimeSpan, cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => StartBackendAsync(cancellationToken);

    public async IAsyncEnumerable<WorkDelivery<MessageWorkItem>> ReadAsync(
        string consumer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await _client.EnsureGroupAsync(cancellationToken);

        await foreach (var prepared in ReadPreparedAsync(consumer, cancellationToken))
        {
            yield return CreateDelivery(consumer, prepared);
        }
    }

    public async IAsyncEnumerable<WorkDelivery<MessageWorkItem>> RecoverAsync(
        string consumer,
        TimeSpan minimumIdleTime,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var prepared in ReclaimPreparedAsync(consumer, minimumIdleTime, count, cancellationToken))
        {
            yield return CreateDelivery(consumer, prepared);
        }
    }

    IAsyncEnumerable<QueuedMessageWorkItem> IMessageWorkQueue.ReadAsync(
        string consumer,
        CancellationToken cancellationToken) =>
        ReadQueuedAsync(consumer, cancellationToken);

    public async IAsyncEnumerable<QueuedMessageWorkItem> ReclaimAsync(
        string consumer,
        TimeSpan minimumIdleTime,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var prepared in ReclaimPreparedAsync(consumer, minimumIdleTime, count, cancellationToken))
        {
            yield return CreateQueuedItem(consumer, prepared);
        }
    }

    public Task CompleteAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken) =>
        item.Lease.CompleteAsync(cancellationToken);

    public async Task<WorkItemFailureResult> FailAsync(
        QueuedMessageWorkItem item,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var action = ShouldDiscard(item.DeliveryCount)
            ? WorkItemFailureAction.Discarded
            : WorkItemFailureAction.Retried;
        await item.Lease.RetryAsync(exception, cancellationToken);
        return new WorkItemFailureResult(action, item.DeliveryCount);
    }

    public Task ClearPendingAsync(CancellationToken cancellationToken)
    {
        if (!_options.ClearPendingOnStartup)
        {
            return Task.CompletedTask;
        }

        return _client.ClearPendingAsync(cancellationToken);
    }

    private async Task StartBackendAsync(CancellationToken cancellationToken)
    {
        await _client.EnsureGroupAsync(cancellationToken);
        await ClearPendingAsync(cancellationToken);
    }

    private async IAsyncEnumerable<QueuedMessageWorkItem> ReadQueuedAsync(
        string consumer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await _client.EnsureGroupAsync(cancellationToken);

        await foreach (var prepared in ReadPreparedAsync(consumer, cancellationToken))
        {
            yield return CreateQueuedItem(consumer, prepared);
        }
    }

    private async IAsyncEnumerable<PreparedDelivery> ReadPreparedAsync(
        string consumer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var leaseToken = CreateLeaseToken();
            var entry = await _client.ReadNewAsync(consumer, leaseToken, cancellationToken);
            if (entry is null)
            {
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                // The remote read may have claimed work. Leave it pending for
                // recovery rather than acknowledging it during cancellation.
                cancellationToken.ThrowIfCancellationRequested();
            }

            var prepared = await PrepareAsync(entry, consumer, leaseToken, cancellationToken);
            if (prepared is not null)
            {
                yield return prepared;
            }
        }
    }

    private async IAsyncEnumerable<PreparedDelivery> ReclaimPreparedAsync(
        string consumer,
        TimeSpan minimumIdleTime,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var index = 0; index < Math.Max(1, count); index++)
        {
            var leaseToken = CreateLeaseToken();
            var entry = await _client.ReclaimAsync(
                consumer,
                minimumIdleTime,
                leaseToken,
                cancellationToken);
            if (entry is null)
            {
                yield break;
            }

            var prepared = await PrepareAsync(entry, consumer, leaseToken, cancellationToken);
            if (prepared is not null)
            {
                yield return prepared;
            }
        }
    }

    private async Task<PreparedDelivery?> PrepareAsync(
        RedisStreamEntry entry,
        string consumer,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        MessageWorkItem item;
        try
        {
            item = MessageWorkItem.Deserialize(entry.Payload);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Discarding malformed work item {EntryId}", entry.EntryId);
            _metrics?.Malformed.Add(1);
            _metrics?.QueueDepth.Add(-1);
            await DiscardMalformedAsync(entry.EntryId, consumer, leaseToken, cancellationToken);
            return null;
        }

        return new PreparedDelivery(
            entry.EntryId,
            item,
            entry.DeliveryCount,
            DateTimeOffset.UtcNow,
            leaseToken);
    }

    private WorkDelivery<MessageWorkItem> CreateDelivery(string consumer, PreparedDelivery prepared) =>
        new(
            prepared.Item,
            prepared.EntryId,
            prepared.Attempt,
            prepared.ReceivedAt,
            CreateLease(consumer, prepared));

    private QueuedMessageWorkItem CreateQueuedItem(string consumer, PreparedDelivery prepared) =>
        new(prepared.EntryId, prepared.Item, CreateLease(consumer, prepared), prepared.Attempt);

    private IWorkItemLease CreateLease(string consumer, PreparedDelivery prepared) =>
        new RedisWorkItemLease(
            _client,
            _options,
            consumer,
            prepared.EntryId,
            prepared.LeaseToken,
            (exception, cancellationToken) => ApplyRetryPolicyAsync(
                consumer,
                prepared.EntryId,
                prepared.LeaseToken,
                prepared.Attempt,
                exception,
                cancellationToken),
            _metrics,
            _logger);

    // One retry/discard policy. Legacy FailAsync delegates to the same lease path.
    private async Task<LeaseOperationResult> ApplyRetryPolicyAsync(
        string consumer,
        string entryId,
        string leaseToken,
        int attempt,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (!ShouldDiscard(attempt))
        {
            _logger.LogWarning(
                exception,
                "Queue entry {EntryId} failed; Redis will retry it after it becomes idle (attempt {Attempt} of {MaxAttempts})",
                entryId,
                attempt,
                Math.Max(1, _options.MaxProcessingAttempts));
            return await _client.RetryAsync(consumer, entryId, leaseToken, cancellationToken);
        }

        _logger.LogError(
            exception,
            "Discarded queue entry {EntryId} after {Attempt} processing attempts",
            entryId,
            attempt);
        return await _client.CompleteAsync(consumer, entryId, leaseToken, cancellationToken);
    }

    private async Task DiscardMalformedAsync(
        string entryId,
        string consumer,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await _client.CompleteAsync(
                    consumer,
                    entryId,
                    leaseToken,
                    cancellationToken);
                if (result is LeaseOperationResult.Applied or LeaseOperationResult.AlreadyApplied)
                {
                    return;
                }

                if (result == LeaseOperationResult.LeaseLost)
                {
                    _logger.LogWarning(
                        "Malformed queue entry {EntryId} was claimed by a newer delivery owner before cleanup",
                        entryId);
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Cancelled malformed work item cleanup for {EntryId}", entryId);
                return;
            }
            catch (Exception exception)
            {
                _metrics?.CleanupFailed.Add(1);
                if (attempt >= Math.Max(1, _redisOptions.MalformedCleanupMaxAttempts))
                {
                    _logger.LogError(
                        exception,
                        "Malformed work item {EntryId} remains pending after atomic cleanup reached its attempt limit",
                        entryId);
                    return;
                }

                _logger.LogWarning(exception, "Retrying malformed work item cleanup for {EntryId}", entryId);
            }

            if (attempt >= Math.Max(1, _redisOptions.MalformedCleanupMaxAttempts))
            {
                _metrics?.CleanupFailed.Add(1);
                _logger.LogError(
                    "Malformed work item {EntryId} remains pending after atomic cleanup reached its attempt limit",
                    entryId);
                return;
            }

            await Task.Delay(
                TimeSpan.FromTicks(Math.Min(_redisOptions.RetryDelay.Ticks, _redisOptions.MalformedCleanupMaxDelay.Ticks)),
                cancellationToken);
        }
    }

    private bool ShouldDiscard(int attempt) => attempt >= Math.Max(1, _options.MaxProcessingAttempts);

    private static string CreateLeaseToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    internal static string ConsumerName(string consumer, string leaseToken) => $"{consumer}|{leaseToken}";
}
