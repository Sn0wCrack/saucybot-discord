using System.Diagnostics;
using System.Diagnostics.Metrics;
using SaucyBot.Diagnostics;
using SaucyBot.Queue.Redis;

namespace SaucyBot.Queue;

public interface IQueuedWorkItemExecutor
{
    Task<QueuedWorkItemExecutionOutcome> ExecuteAsync(
        string consumer,
        QueuedMessageWorkItem item,
        CancellationToken cancellationToken);
}

public enum QueuedWorkItemExecutionOutcome
{
    Completed,
    Failed,
    Cancelled,
    LeaseLost,
}

public sealed class QueuedWorkItemExecutor : IQueuedWorkItemExecutor
{
    private readonly IWorkItemProcessor _processor;
    private readonly IMessageWorkQueue _queue;
    private readonly IRedisStreamClient _redis;
    private readonly WorkQueueOptions _options;
    private readonly ILogger<QueuedWorkItemExecutor> _logger;
    private readonly ISaucyBotMetrics _metrics;

    public QueuedWorkItemExecutor(
        IWorkItemProcessor processor,
        IMessageWorkQueue queue,
        IRedisStreamClient redis,
        WorkQueueOptions options,
        ILogger<QueuedWorkItemExecutor> logger,
        ISaucyBotMetrics metrics)
    {
        _processor = processor;
        _queue = queue;
        _redis = redis;
        _options = options;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<QueuedWorkItemExecutionOutcome> ExecuteAsync(
        string consumer,
        QueuedMessageWorkItem item,
        CancellationToken cancellationToken)
    {
        using var processingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        processingCancellation.CancelAfter(_options.MaxProcessingTime);

        var leaseState = new LeaseState(_metrics, consumer);
        var heartbeat = RunHeartbeatAsync(
            consumer,
            item.EntryId,
            processingCancellation,
            leaseState);

        Exception? failure = null;
        var completed = false;

        try
        {
            await _processor.ProcessAsync(item, processingCancellation.Token);
            processingCancellation.Token.ThrowIfCancellationRequested();
            completed = true;
        }
        catch (OperationCanceledException) when (processingCancellation.IsCancellationRequested)
        {
            _metrics.Cancelled.Add(1);
            _logger.LogDebug("Cancelled processing queue entry {EntryId}", item.EntryId);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            processingCancellation.Cancel();
            await heartbeat;
        }

        if (leaseState.IsLost)
        {
            _logger.LogWarning("Skipping completion for queue entry {EntryId} after lease loss", item.EntryId);
            return QueuedWorkItemExecutionOutcome.LeaseLost;
        }

        if (failure is not null)
        {
            _metrics.Failed.Add(1);
            try
            {
                var result = await _queue.FailAsync(item, failure, CancellationToken.None);
                _logger.LogDebug(
                    "Handled failed queue entry {EntryId} with action {Action} at attempt {Attempt}",
                    item.EntryId,
                    result.Action,
                    result.Attempt);
            }
            catch (Exception cleanupException)
            {
                _metrics.CleanupFailed.Add(1);
                _logger.LogError(
                    cleanupException,
                    "Failed to handle failed queue entry {EntryId}",
                    item.EntryId);
            }

            return QueuedWorkItemExecutionOutcome.Failed;
        }

        if (!completed)
        {
            return QueuedWorkItemExecutionOutcome.Cancelled;
        }

        try
        {
            await _queue.CompleteAsync(item, CancellationToken.None);
            _metrics.Succeeded.Add(1);
            return QueuedWorkItemExecutionOutcome.Completed;
        }
        catch (Exception cleanupException)
        {
            _metrics.CleanupFailed.Add(1);
            _logger.LogError(
                cleanupException,
                "Failed to complete queue entry {EntryId}",
                item.EntryId);
            return QueuedWorkItemExecutionOutcome.Failed;
        }
    }

    private async Task RunHeartbeatAsync(
        string consumer,
        string entryId,
        CancellationTokenSource processingCancellation,
        LeaseState leaseState)
    {
        using var timer = new PeriodicTimer(_options.HeartbeatInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(processingCancellation.Token))
            {
                var renewed = await _redis.RenewAsync(
                    consumer,
                    entryId,
                    processingCancellation.Token);

                if (renewed)
                {
                    _metrics.LeaseRenewed.Add(1, LeaseTags(consumer));
                    continue;
                }

                leaseState.MarkLost();
                processingCancellation.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (processingCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            leaseState.MarkLost();
            _logger.LogWarning(exception, "Failed to renew lease for queue entry {EntryId}", entryId);
            processingCancellation.Cancel();
        }
    }

    private sealed class LeaseState(ISaucyBotMetrics metrics, string consumer)
    {
        private int _lost;

        public bool IsLost => Volatile.Read(ref _lost) != 0;

        public void MarkLost()
        {
            if (Interlocked.Exchange(ref _lost, 1) == 0)
            {
                metrics.LeaseLost.Add(1, LeaseTags(consumer));
            }
        }
    }

    private static TagList LeaseTags(string consumer) => new()
    {
        { "work_type", "message" },
        { "consumer_type", consumer.EndsWith("-recovery", StringComparison.Ordinal) ? "recovery" : "normal" },
    };
}
