using SaucyBot.Diagnostics;

namespace SaucyBot.Queue;

public interface IQueuedWorkItemExecutor
{
    Task ExecuteAsync(
        string consumer,
        QueuedMessageWorkItem item,
        CancellationToken cancellationToken);
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

    public async Task ExecuteAsync(
        string consumer,
        QueuedMessageWorkItem item,
        CancellationToken cancellationToken)
    {
        using var processingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        processingCancellation.CancelAfter(_options.MaxProcessingTime);

        var leaseLost = 0;
        var heartbeat = RunHeartbeatAsync(
            consumer,
            item.EntryId,
            processingCancellation,
            () =>
            {
                if (Interlocked.Exchange(ref leaseLost, 1) == 0)
                {
                    _metrics.LeaseLost.Add(1);
                }
            });

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

        if (Volatile.Read(ref leaseLost) != 0)
        {
            _logger.LogWarning("Skipping completion for queue entry {EntryId} after lease loss", item.EntryId);
            return;
        }

        if (failure is not null)
        {
            _metrics.Failed.Add(1);
            var result = await _queue.FailAsync(item, failure, CancellationToken.None);
            _logger.LogDebug(
                "Handled failed queue entry {EntryId} with action {Action} at attempt {Attempt}",
                item.EntryId,
                result.Action,
                result.Attempt);
            return;
        }

        if (!completed)
        {
            return;
        }

        await _queue.CompleteAsync(item, CancellationToken.None);
        _metrics.Succeeded.Add(1);
    }

    private async Task RunHeartbeatAsync(
        string consumer,
        string entryId,
        CancellationTokenSource processingCancellation,
        Action markLeaseLost)
    {
        using var timer = new PeriodicTimer(_options.Redis.HeartbeatInterval);

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
                    _metrics.LeaseRenewed.Add(1);
                    continue;
                }

                markLeaseLost();
                processingCancellation.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (processingCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            markLeaseLost();
            _logger.LogWarning(exception, "Failed to renew lease for queue entry {EntryId}", entryId);
            processingCancellation.Cancel();
        }
    }
}
