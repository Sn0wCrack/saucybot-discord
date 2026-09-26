using System.Diagnostics;
using SaucyBot.Diagnostics;

namespace SaucyBot.Queue;

public sealed class MessageQueueWorker
{
    private static readonly TimeSpan HandlerCancellationGracePeriod = TimeSpan.FromMilliseconds(10);
    private readonly MessageDeliveryChannel _deliveries;
    private readonly IQueueMiddlewarePipeline<MessageWorkItem> _pipeline;
    private readonly IWorkItemProcessor _processor;
    private readonly WorkQueueOptions _options;
    private readonly ILogger<MessageQueueWorker> _logger;
    private readonly ISaucyBotMetrics _metrics;

    public MessageQueueWorker(
        MessageDeliveryChannel deliveries,
        IQueueMiddlewarePipeline<MessageWorkItem> pipeline,
        IWorkItemProcessor processor,
        WorkQueueOptions options,
        ILogger<MessageQueueWorker> logger,
        ISaucyBotMetrics metrics)
    {
        _deliveries = deliveries;
        _pipeline = pipeline;
        _processor = processor;
        _options = options;
        _logger = logger;
        _metrics = metrics;
    }

    private async Task RunAsync(string consumer, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var delivery in _deliveries.ReadAllAsync(cancellationToken))
            {
                await ProcessOneAsync(consumer, delivery, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Message worker {Consumer} stopped", consumer);
        }
    }

    internal async Task RunSupervisedAsync(string consumer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(consumer, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Message worker {Consumer} failed; restarting", consumer);
                _metrics.WorkerRestarts.Add(1, new KeyValuePair<string, object?>("worker_type", "message"));

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task ProcessOneAsync(
        string consumer,
        WorkDelivery<MessageWorkItem> delivery,
        CancellationToken cancellationToken)
    {
        _metrics.Dequeued.Add(1);
        if (!delivery.IsRecovered)
        {
            _metrics.QueueDepth.Add(-1);
        }

        if (delivery.Item.EnqueuedAt != default)
        {
            _metrics.QueueAge.Record((DateTimeOffset.UtcNow - delivery.Item.EnqueuedAt).TotalMilliseconds);
        }

        _metrics.ActiveWorkers.Add(1);
        using var activity = QueueTelemetry.ActivitySource.StartActivity(ActivityKind.Consumer);
        activity?.SetTag("saucybot.work.type", "message");
        activity?.SetTag("saucybot.queue.consumer", consumer);
        activity?.SetTag("saucybot.queue.entry_id", delivery.DeliveryId);

        try
        {
            var outcome = await ProcessDeliveryAsync(delivery, cancellationToken);
            switch (outcome)
            {
                case MessageWorkResult.Completed:
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    _logger.LogDebug("Message worker {Consumer} completed queue entry {DeliveryId}", consumer, delivery.DeliveryId);
                    break;
                case MessageWorkResult.Failed:
                    activity?.SetStatus(ActivityStatusCode.Error, "message processing failed");
                    break;
                case MessageWorkResult.Cancelled:
                    activity?.SetTag("saucybot.cancelled", true);
                    break;
                case MessageWorkResult.LeaseLost:
                    activity?.SetTag("saucybot.lease_lost", true);
                    break;
                case MessageWorkResult.OutcomeUnknown:
                    activity?.SetTag("saucybot.outcome_unknown", true);
                    break;
            }
        }
        finally
        {
            _metrics.ActiveWorkers.Add(-1);
        }
    }

    private async Task<MessageWorkResult> ProcessDeliveryAsync(
        WorkDelivery<MessageWorkItem> delivery,
        CancellationToken stoppingToken)
    {
        await using var lease = delivery.Lease;
        using var deadline = new CancellationTokenSource(_options.MaxProcessingTime);
        using var processing = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            lease.LostToken,
            deadline.Token);

        var context = new QueueWorkContext<MessageWorkItem>(
            delivery.Item,
            delivery.DeliveryId,
            delivery.Attempt,
            delivery.ReceivedAt);

        try
        {
            var handler = _pipeline.InvokeAsync(context, HandleDeliveryAsync, processing.Token);
            var deadlineTask = Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token);
            await Task.WhenAny(handler, deadlineTask);
            var handlerOverdue = false;
            if (deadline.IsCancellationRequested && !handler.IsCompleted)
            {
                // Allow cooperative cancellation continuations to complete before recording overdue work.
                await Task.WhenAny(handler, Task.Delay(HandlerCancellationGracePeriod));
                handlerOverdue = !handler.IsCompleted;
                if (handlerOverdue)
                {
                    _metrics.HandlerOverdue.Add(1, QueueMetricTags.Handler("message"));
                }
            }

            await handler;
            if (handlerOverdue)
            {
                _logger.LogWarning(
                    "Message handler remained active after its processing deadline for {DeliveryId}",
                    delivery.DeliveryId);
            }

            processing.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (lease.LostToken.IsCancellationRequested)
        {
            return RecordLeaseLost();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return MessageWorkResult.Cancelled;
        }
        catch (OperationCanceledException) when (processing.IsCancellationRequested)
        {
            return MessageWorkResult.Cancelled;
        }
        catch (Exception exception)
        {
            return await RetryFailedDeliveryAsync(lease, delivery, exception);
        }

        try
        {
            var result = await lease.CompleteAsync(CancellationToken.None);
            return result switch
            {
                LeaseOperationResult.Applied or LeaseOperationResult.AlreadyApplied => CompleteSuccessfully(),
                LeaseOperationResult.LeaseLost => RecordLeaseLost(),
                _ => RecordUnknownOutcome(),
            };
        }
        catch (Exception exception)
        {
            _metrics.CleanupFailed.Add(1);
            _logger.LogWarning(exception, "Completion outcome is unknown for queue entry {DeliveryId}", delivery.DeliveryId);
            return RecordUnknownOutcome();
        }

        MessageWorkResult CompleteSuccessfully()
        {
            return MessageWorkResult.Completed;
        }

        MessageWorkResult RecordLeaseLost()
        {
            _metrics.LeaseLost.Add(1, QueueMetricTags.Lease("message"));
            _logger.LogWarning("Message worker lost the lease for queue entry {DeliveryId}", delivery.DeliveryId);
            return MessageWorkResult.LeaseLost;
        }

        MessageWorkResult RecordUnknownOutcome()
        {
            _logger.LogWarning("Queue operation outcome is unknown for queue entry {DeliveryId}", delivery.DeliveryId);
            return MessageWorkResult.OutcomeUnknown;
        }
    }

    private async Task<MessageWorkResult> RetryFailedDeliveryAsync(
        IWorkItemLease lease,
        WorkDelivery<MessageWorkItem> delivery,
        Exception exception)
    {
        try
        {
            var result = await lease.RetryAsync(exception, CancellationToken.None);
            return result switch
            {
                LeaseOperationResult.LeaseLost => RecordLeaseLost(delivery),
                LeaseOperationResult.OutcomeUnknown => RecordUnknownOutcome(delivery),
                _ => MessageWorkResult.Failed,
            };
        }
        catch (Exception retryException)
        {
            _metrics.CleanupFailed.Add(1);
            _logger.LogError(retryException, "Failed to retry queue entry {DeliveryId}", delivery.DeliveryId);
            return RecordUnknownOutcome(delivery);
        }

        MessageWorkResult RecordLeaseLost(WorkDelivery<MessageWorkItem> work)
        {
            _metrics.LeaseLost.Add(1, QueueMetricTags.Lease("message"));
            _logger.LogWarning("Message worker lost the lease for queue entry {DeliveryId}", work.DeliveryId);
            return MessageWorkResult.LeaseLost;
        }

        MessageWorkResult RecordUnknownOutcome(WorkDelivery<MessageWorkItem> work)
        {
            _logger.LogWarning("Queue operation outcome is unknown for queue entry {DeliveryId}", work.DeliveryId);
            return MessageWorkResult.OutcomeUnknown;
        }
    }

    private Task HandleDeliveryAsync(
        QueueWorkContext<MessageWorkItem> context,
        CancellationToken cancellationToken) =>
        _processor.ProcessAsync(context.Item, cancellationToken);

    private enum MessageWorkResult
    {
        Completed,
        Failed,
        Cancelled,
        LeaseLost,
        OutcomeUnknown,
    }
}
