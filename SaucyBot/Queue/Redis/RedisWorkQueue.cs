using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Diagnostics;

namespace SaucyBot.Queue.Redis;

public sealed class RedisWorkQueue : IMessageWorkQueue, IWorkItemProducer<MessageWorkItem>, IWorkItemConsumer<MessageWorkItem>
{
    internal const string PayloadField = "payload";

    private enum MalformedCleanupOperation
    {
        Acknowledge,
        Delete,
    }

    private sealed record PreparedDelivery(string EntryId, MessageWorkItem Item, int Attempt, DateTimeOffset ReceivedAt);

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
        var action = await ApplyRetryPolicyAsync(
            item.Lease,
            item.EntryId,
            item.DeliveryCount,
            exception,
            cancellationToken);
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
            var entry = await _client.ReadNewAsync(consumer, cancellationToken);
            if (entry is null)
            {
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await _client.AcknowledgeAsync(entry.EntryId, CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var prepared = await PrepareAsync(entry, cancellationToken);
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
        var entries = await _client.ReclaimAsync(
            consumer,
            minimumIdleTime,
            count,
            cancellationToken);

        foreach (var entry in entries)
        {
            var prepared = await PrepareAsync(entry, cancellationToken);
            if (prepared is not null)
            {
                yield return prepared;
            }
        }
    }

    private async Task<PreparedDelivery?> PrepareAsync(RedisStreamEntry entry, CancellationToken cancellationToken)
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
            await DiscardMalformedAsync(entry.EntryId, cancellationToken);
            return null;
        }

        return new PreparedDelivery(entry.EntryId, item, entry.DeliveryCount, DateTimeOffset.UtcNow);
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
        new DeliveryLease(this, consumer, prepared.EntryId, prepared.Attempt);

    // One retry/discard policy. Legacy FailAsync and lease RetryAsync both run it.
    private async Task<WorkItemFailureAction> ApplyRetryPolicyAsync(
        IWorkItemLease lease,
        string entryId,
        int attempt,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.MaxProcessingAttempts);
        if (attempt < maxAttempts)
        {
            _logger.LogWarning(
                exception,
                "Queue entry {EntryId} failed; Redis will retry it after it becomes idle (attempt {Attempt} of {MaxAttempts})",
                entryId,
                attempt,
                maxAttempts);
            await lease.DisposeAsync();
            return WorkItemFailureAction.Retried;
        }

        _logger.LogError(
            exception,
            "Discarded queue entry {EntryId} after {Attempt} processing attempts",
            entryId,
            attempt);
        await lease.CompleteAsync(cancellationToken);
        return WorkItemFailureAction.Discarded;
    }

    private async Task CompleteEntryAsync(string entryId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _client.AcknowledgeAsync(entryId, CancellationToken.None);
        try
        {
            await _client.DeleteAsync(entryId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _metrics?.CleanupFailed.Add(1);
            _logger.LogError(exception, "Acknowledged work item {EntryId} but failed to delete it", entryId);
            throw;
        }
    }

    private async Task DiscardMalformedAsync(string entryId, CancellationToken cancellationToken)
    {
        var acknowledged = await RetryMalformedCleanupAsync(
            entryId,
            MalformedCleanupOperation.Acknowledge,
            () => _client.AcknowledgeAsync(entryId, CancellationToken.None),
            cancellationToken);

        if (!acknowledged)
        {
            return;
        }

        await RetryMalformedCleanupAsync(
            entryId,
            MalformedCleanupOperation.Delete,
            () => _client.DeleteAsync(entryId, CancellationToken.None),
            cancellationToken);
    }

    private async Task<bool> RetryMalformedCleanupAsync(
        string entryId,
        MalformedCleanupOperation operation,
        Func<Task> cleanup,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await cleanup();
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Cancelled malformed work item cleanup {Operation} for {EntryId}", operation, entryId);
                return false;
            }
            catch (Exception exception)
            {
                _metrics?.CleanupFailed.Add(1);
                if (attempt >= Math.Max(1, _redisOptions.MalformedCleanupMaxAttempts))
                {
                    _logger.LogError(
                        exception,
                        "Malformed work item {EntryId} remains pending after cleanup operation {Operation} reached its attempt limit",
                        entryId,
                        operation);
                    return false;
                }

                _logger.LogWarning(exception, "Retrying malformed work item cleanup {Operation} for {EntryId}", operation, entryId);
                await Task.Delay(
                    TimeSpan.FromTicks(Math.Min(_redisOptions.RetryDelay.Ticks, _redisOptions.MalformedCleanupMaxDelay.Ticks)),
                    cancellationToken);
            }
        }
    }

    private sealed class DeliveryLease : IWorkItemLease
    {
        private readonly RedisWorkQueue _owner;
        private readonly string _consumer;
        private readonly string _entryId;
        private readonly int _attempt;
        private readonly CancellationTokenSource _renewalStop = new();
        private readonly CancellationTokenSource _lost = new();
        private readonly CancellationToken _lostToken;
        private readonly Task _renewal;
        private int _renewalStopped;

        public DeliveryLease(RedisWorkQueue owner, string consumer, string entryId, int attempt)
        {
            _owner = owner;
            _consumer = consumer;
            _entryId = entryId;
            _attempt = attempt;
            _lostToken = _lost.Token;
            _renewal = RenewAsync();
        }

        public CancellationToken LostToken => _lostToken;

        public async Task<LeaseOperationResult> CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StopRenewalAsync();
            if (IsLost)
            {
                return LeaseOperationResult.LeaseLost;
            }

            await _owner.CompleteEntryAsync(_entryId, CancellationToken.None);
            return LeaseOperationResult.Applied;
        }

        public async Task<LeaseOperationResult> RetryAsync(Exception exception, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StopRenewalAsync();
            if (IsLost)
            {
                return LeaseOperationResult.LeaseLost;
            }

            await _owner.ApplyRetryPolicyAsync(this, _entryId, _attempt, exception, cancellationToken);
            return LeaseOperationResult.Applied;
        }

        public async ValueTask DisposeAsync()
        {
            await StopRenewalAsync();
            _renewalStop.Dispose();
            _lost.Dispose();
        }

        private bool IsLost => _lost.IsCancellationRequested;

        private async Task RenewAsync()
        {
            using var timer = new PeriodicTimer(_owner._options.HeartbeatInterval);
            using var renewalLifetime = CancellationTokenSource.CreateLinkedTokenSource(_renewalStop.Token);
            // The renewal deadline preserves MaxProcessingTime even when a handler ignores cancellation.
            renewalLifetime.CancelAfter(_owner._options.MaxProcessingTime);
            try
            {
                while (await timer.WaitForNextTickAsync(renewalLifetime.Token))
                {
                    var renewed = await _owner._client.RenewAsync(_consumer, _entryId, _renewalStop.Token);
                    if (!renewed)
                    {
                        MarkLost();
                        return;
                    }

                    _owner._metrics?.LeaseRenewed.Add(1, QueueMetricTags.Lease(_consumer));
                }
            }
            catch (OperationCanceledException) when (_renewalStop.IsCancellationRequested || renewalLifetime.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _owner._logger.LogWarning(
                    exception,
                    "Failed to renew the lease for queue entry {EntryId}; the delivery is considered lost",
                    _entryId);
                MarkLost();
            }
        }

        private void MarkLost()
        {
            if (!_lost.IsCancellationRequested)
            {
                _lost.Cancel();
            }
        }

        private async Task StopRenewalAsync()
        {
            if (Interlocked.Exchange(ref _renewalStopped, 1) == 0)
            {
                _renewalStop.Cancel();
            }

            await _renewal;
        }
    }
}
