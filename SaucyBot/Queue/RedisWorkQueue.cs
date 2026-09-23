using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using SaucyBot.Diagnostics;

namespace SaucyBot.Queue;

public sealed class RedisWorkQueue(
    IRedisStreamClient client,
    WorkQueueOptions options,
    ISaucyBotMetrics? metrics = null,
    ILogger<RedisWorkQueue>? logger = null) : IMessageWorkQueue
{
    private enum MalformedCleanupOperation
    {
        Acknowledge,
        Delete,
    }

    internal const string PayloadField = "payload";
    private readonly IRedisStreamClient _client = client;
    private readonly WorkQueueOptions _options = options;
    private readonly RedisWorkQueueOptions _redisOptions = options.Redis;
    private readonly ISaucyBotMetrics? _metrics = metrics;
    private readonly ILogger<RedisWorkQueue> _logger = logger ?? NullLogger<RedisWorkQueue>.Instance;

    public async Task EnqueueAsync(MessageWorkItem item, CancellationToken cancellationToken)
    {
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
                    return;
                }
                catch (RedisBackpressureException)
                {
                    _metrics?.Retried.Add(1);
                    _logger.LogDebug(
                        "Redis queue is applying backpressure; retrying enqueue after {RetryDelay}",
                        _redisOptions.RetryDelay);
                    await Task.Delay(_redisOptions.RetryDelay, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _metrics?.Cancelled.Add(1);
            throw;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        ClearPendingAsync(cancellationToken);

    public async IAsyncEnumerable<QueuedMessageWorkItem> ReadAsync(
        string consumer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await _client.EnsureGroupAsync(cancellationToken);

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
                continue;
            }

            yield return new QueuedMessageWorkItem(entry.EntryId, item, entry.DeliveryCount);
        }
    }

    public async IAsyncEnumerable<QueuedMessageWorkItem> ReclaimAsync(
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
            MessageWorkItem item;
            try
            {
                item = MessageWorkItem.Deserialize(entry.Payload);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Discarding malformed recovered work item {EntryId}", entry.EntryId);
                _metrics?.Malformed.Add(1);
                _metrics?.QueueDepth.Add(-1);
                await DiscardMalformedAsync(entry.EntryId, cancellationToken);
                continue;
            }

            yield return new QueuedMessageWorkItem(entry.EntryId, item, entry.DeliveryCount);
        }
    }

    public async Task CompleteAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _client.AcknowledgeAsync(item.EntryId, CancellationToken.None);
        try
        {
            await _client.DeleteAsync(item.EntryId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _metrics?.CleanupFailed.Add(1);
            _logger.LogError(exception, "Acknowledged work item {EntryId} but failed to delete it", item.EntryId);
            throw;
        }
    }

    public async Task<WorkItemFailureResult> FailAsync(
        QueuedMessageWorkItem item,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.MaxProcessingAttempts);
        if (item.DeliveryCount < maxAttempts)
        {
            _logger.LogWarning(
                exception,
                "Queue entry {EntryId} failed; Redis will retry it after it becomes idle (attempt {Attempt} of {MaxAttempts})",
                item.EntryId,
                item.DeliveryCount,
                maxAttempts);
            return new WorkItemFailureResult(WorkItemFailureAction.Retried, item.DeliveryCount);
        }

        await CompleteAsync(item, cancellationToken);
        _logger.LogError(
            exception,
            "Discarded queue entry {EntryId} after {Attempt} processing attempts",
            item.EntryId,
            item.DeliveryCount);
        return new WorkItemFailureResult(WorkItemFailureAction.Discarded, item.DeliveryCount);
    }

    public Task ClearPendingAsync(CancellationToken cancellationToken)
    {
        if (!_options.ClearPendingOnStartup)
        {
            return Task.CompletedTask;
        }

        return _client.ClearPendingAsync(cancellationToken);
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
}
