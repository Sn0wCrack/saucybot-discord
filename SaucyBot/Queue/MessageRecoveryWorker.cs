using System.Threading.Channels;
using SaucyBot.Diagnostics;

namespace SaucyBot.Queue;

public sealed class MessageRecoveryWorker
{
    private readonly IWorkItemConsumer<MessageWorkItem> _consumer;
    private readonly MessageDeliveryChannel _deliveries;
    private readonly WorkQueueOptions _options;
    private readonly ILogger<MessageRecoveryWorker> _logger;
    private readonly ISaucyBotMetrics _metrics;

    public MessageRecoveryWorker(
        IWorkItemConsumer<MessageWorkItem> consumer,
        MessageDeliveryChannel deliveries,
        WorkQueueOptions options,
        ILogger<MessageRecoveryWorker> logger,
        ISaucyBotMetrics metrics)
    {
        _consumer = consumer;
        _deliveries = deliveries;
        _options = options;
        _logger = logger;
        _metrics = metrics;
    }

    internal async Task RunAsync(string consumer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var reservation = await _deliveries.ReserveAsync(cancellationToken);
                await using var recovered = _consumer.RecoverAsync(
                        consumer,
                        _options.PendingMessageIdleTime,
                        count: 1,
                        cancellationToken: cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                if (!await recovered.MoveNextAsync())
                {
                    await Task.Delay(_options.ReclaimerInterval, cancellationToken);
                    continue;
                }

                var delivery = recovered.Current;
                _metrics.Reclaimed.Add(1, new KeyValuePair<string, object?>("consumer_type", "recovery"));
                if (!reservation.Publish(delivery))
                {
                    await delivery.Lease.DisposeAsync();
                    return;
                }

                await Task.Delay(_options.ReclaimerInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ChannelClosedException)
            {
                return;
            }
            catch (Exception exception)
            {
                if (exception is TimeoutException)
                {
                    _metrics.BackendOperationTimedOut.Add(
                        1,
                        QueueMetricTags.BackendOperation("recovery"));
                }

                _logger.LogError(exception, "Queue recovery worker {Consumer} failed", consumer);
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
}
