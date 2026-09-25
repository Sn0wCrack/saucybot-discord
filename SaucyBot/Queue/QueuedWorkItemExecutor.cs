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
                await lease.CompleteAsync(CancellationToken.None);
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
        finally
        {
            await lease.DisposeAsync();
        }
    }
}
