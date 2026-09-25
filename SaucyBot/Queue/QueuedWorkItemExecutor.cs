using SaucyBot.Diagnostics;

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
    OutcomeUnknown,
}

public sealed class QueuedWorkItemExecutor : IQueuedWorkItemExecutor
{
    private readonly IWorkItemProcessor _processor;
    private readonly WorkQueueOptions _options;
    private readonly ILogger<QueuedWorkItemExecutor> _logger;
    private readonly ISaucyBotMetrics _metrics;

    public QueuedWorkItemExecutor(
        IWorkItemProcessor processor,
        WorkQueueOptions options,
        ILogger<QueuedWorkItemExecutor> logger,
        ISaucyBotMetrics metrics)
    {
        _processor = processor;
        _options = options;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<QueuedWorkItemExecutionOutcome> ExecuteAsync(
        string consumer,
        QueuedMessageWorkItem item,
        CancellationToken cancellationToken)
    {
        var lease = item.Lease;
        using var processingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.LostToken);
        processingCancellation.CancelAfter(_options.MaxProcessingTime);

        Exception? failure = null;
        var completed = false;

        // A handler that ignores cancellation stays awaited here. The lease
        // stops renewing at MaxProcessingTime, so the item is recovered while
        // this task remains observable and attached to its worker.
        var processing = RunHandlerAsync(item, processingCancellation.Token);
        using var overdueDeadline = new CancellationTokenSource(_options.MaxProcessingTime);
        using var overdueRegistration = overdueDeadline.Token.Register(() =>
        {
            if (!processing.IsCompleted)
            {
                _logger.LogWarning(
                    "Queue entry {EntryId} handler is overdue and still runs after {MaxProcessingTime}; the worker keeps awaiting it and the item stays pending for recovery",
                    item.EntryId,
                    _options.MaxProcessingTime);
            }
        });

        try
        {
            await processing;
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

        try
        {
            if (lease.LostToken.IsCancellationRequested)
            {
                _metrics.LeaseLost.Add(1, QueueMetricTags.Lease(consumer));
                _logger.LogWarning("Skipping completion for queue entry {EntryId} after lease loss", item.EntryId);
                return QueuedWorkItemExecutionOutcome.LeaseLost;
            }

            if (failure is not null)
            {
                _metrics.Failed.Add(1);
                try
                {
                    var result = await lease.RetryAsync(failure, CancellationToken.None);
                    _logger.LogDebug(
                        "Handled failed queue entry {EntryId} with lease result {Result}",
                        item.EntryId,
                        result);
                    if (result == LeaseOperationResult.LeaseLost)
                    {
                        _metrics.LeaseLost.Add(1, QueueMetricTags.Lease(consumer));
                        _logger.LogWarning(
                            "Skipping retry handling for queue entry {EntryId} after lease loss",
                            item.EntryId);
                        return QueuedWorkItemExecutionOutcome.LeaseLost;
                    }

                    if (result == LeaseOperationResult.OutcomeUnknown)
                    {
                        _logger.LogWarning(
                            "Queue entry {EntryId} retry outcome is unknown; the item stays pending for recovery and the handler is not rerun here",
                            item.EntryId);
                        return QueuedWorkItemExecutionOutcome.OutcomeUnknown;
                    }
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
                var result = await lease.CompleteAsync(CancellationToken.None);
                switch (result)
                {
                    case LeaseOperationResult.Applied:
                    case LeaseOperationResult.AlreadyApplied:
                        _metrics.Succeeded.Add(1);
                        return QueuedWorkItemExecutionOutcome.Completed;
                    case LeaseOperationResult.LeaseLost:
                        _metrics.LeaseLost.Add(1, QueueMetricTags.Lease(consumer));
                        _logger.LogWarning(
                            "Skipping success for queue entry {EntryId} after lease loss",
                            item.EntryId);
                        return QueuedWorkItemExecutionOutcome.LeaseLost;
                    default:
                        _logger.LogWarning(
                            "Queue entry {EntryId} completion outcome is unknown; success is not reported and the item stays pending for recovery",
                            item.EntryId);
                        return QueuedWorkItemExecutionOutcome.OutcomeUnknown;
                }
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
        finally
        {
            await lease.DisposeAsync();
        }
    }

    // Wrapping the call keeps synchronous handler exceptions observable as a
    // faulted task for the overdue check.
    private async Task RunHandlerAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken) =>
        await _processor.ProcessAsync(item.ToDelivery(), cancellationToken);
}
