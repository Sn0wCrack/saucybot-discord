using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Diagnostics;

namespace SaucyBot.Queue.Redis;

public sealed class RedisWorkQueue : IWorkItemProducer<MessageWorkItem>, IWorkItemConsumer<MessageWorkItem>
{
    internal const string PayloadField = "payload";

    private sealed record PreparedDelivery(
        string EntryId,
        MessageWorkItem Item,
        int Attempt,
        DateTimeOffset ReceivedAt,
        string LeaseToken,
        bool IsRecovered);

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
                // Timeout.InfiniteTimeSpan is -1 ms and must never count as expired.
                var remaining = deadline is null ? Timeout.InfiniteTimeSpan : deadline.Value - DateTimeOffset.UtcNow;
                if (remaining != Timeout.InfiniteTimeSpan && remaining <= TimeSpan.Zero)
                {
                    return LogEnqueueTimedOut(timeout);
                }

                try
                {
                    var add = _client.AddAsync(item.Serialize(), cancellationToken);
                    // An abandoned add can still fault later; keep it observed.
                    _ = add.ContinueWith(
                        static task => _ = task.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    if (remaining == Timeout.InfiniteTimeSpan)
                    {
                        await add.WaitAsync(cancellationToken);
                    }
                    else
                    {
                        await add.WaitAsync(remaining, cancellationToken);
                    }

                    _metrics?.Enqueued.Add(1);
                    _metrics?.QueueDepth.Add(1);
                    return EnqueueResult.Accepted;
                }
                catch (RedisEnqueueAmbiguousException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Queue enqueue has an unknown outcome after {Timeout}; Redis can still accept the command after the caller stops waiting, so the caller must not retry automatically",
                        timeout);
                    return EnqueueResult.TimedOut;
                }
                catch (TimeoutException)
                {
                    return LogEnqueueTimedOut(timeout);
                }
                catch (RedisBackpressureException)
                {
                    _metrics?.Retried.Add(1);
                    var delay = _redisOptions.RetryDelay;
                    if (deadline is not null)
                    {
                        var remainingDelay = deadline.Value - DateTimeOffset.UtcNow;
                        if (remainingDelay <= TimeSpan.Zero)
                        {
                            return LogEnqueueTimedOut(timeout);
                        }

                        if (delay > remainingDelay)
                        {
                            delay = remainingDelay;
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

    private EnqueueResult LogEnqueueTimedOut(TimeSpan timeout)
    {
        _logger.LogWarning(
            "Queue enqueue timed out after {Timeout}; Redis can still accept the command after the caller stops waiting, so the caller must not retry automatically",
            timeout);
        return EnqueueResult.TimedOut;
    }

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

    private async IAsyncEnumerable<PreparedDelivery> ReadPreparedAsync(
        string consumer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leaseToken = CreateLeaseToken();
            var entry = await _client.ReadNewAsync(consumer, leaseToken, cancellationToken);
            if (entry is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                // The remote read may have claimed work. Leave it pending for
                // recovery rather than acknowledging it during cancellation.
                cancellationToken.ThrowIfCancellationRequested();
            }

            var prepared = await PrepareAsync(entry, consumer, leaseToken, isRecovered: false, cancellationToken: cancellationToken);
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
            cancellationToken.ThrowIfCancellationRequested();
            var leaseToken = CreateLeaseToken();
            var entry = await _client.ReclaimAsync(
                consumer,
                minimumIdleTime,
                leaseToken,
                cancellationToken);
            if (entry is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var prepared = await PrepareAsync(entry, consumer, leaseToken, isRecovered: true, cancellationToken: cancellationToken);
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
        bool isRecovered,
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
            if (!isRecovered)
            {
                _metrics?.QueueDepth.Add(-1);
            }

            await DiscardMalformedAsync(entry.EntryId, consumer, leaseToken, cancellationToken);
            return null;
        }

        return new PreparedDelivery(
            entry.EntryId,
            item,
            entry.DeliveryCount,
            DateTimeOffset.UtcNow,
            leaseToken,
            isRecovered);
    }

    private WorkDelivery<MessageWorkItem> CreateDelivery(string consumer, PreparedDelivery prepared) =>
        new(
            prepared.Item,
            prepared.EntryId,
            prepared.Attempt,
            prepared.ReceivedAt,
            CreateLease(consumer, prepared),
            prepared.IsRecovered);

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
                // Cleanup is repeatable and safe to abandon on cancellation, so
                // its bound follows both the operation timeout and the caller.
                var result = await _client
                    .CompleteAsync(consumer, entryId, leaseToken, cancellationToken)
                    .WaitAsync(_options.BackendOperationTimeout, cancellationToken);
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
