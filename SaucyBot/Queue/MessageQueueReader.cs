using System.Threading.Channels;

namespace SaucyBot.Queue;

public sealed class MessageQueueReader
{
    private readonly IWorkItemConsumer<MessageWorkItem> _consumer;
    private readonly MessageDeliveryChannel _deliveries;
    private readonly ILogger<MessageQueueReader> _logger;

    public MessageQueueReader(
        IWorkItemConsumer<MessageWorkItem> consumer,
        MessageDeliveryChannel deliveries,
        ILogger<MessageQueueReader> logger)
    {
        _consumer = consumer;
        _deliveries = deliveries;
        _logger = logger;
    }

    internal async Task RunAsync(string consumer, CancellationToken cancellationToken)
    {
        try
        {
            await using var messages = _consumer.ReadAsync(consumer, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                await using var reservation = await _deliveries.ReserveAsync(cancellationToken);
                bool hasMessage;
                try
                {
                    hasMessage = await messages.MoveNextAsync();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!hasMessage)
                {
                    return;
                }

                var delivery = messages.Current;
                if (!reservation.Publish(delivery))
                {
                    await delivery.Lease.DisposeAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Message queue reader {Consumer} stopped", consumer);
        }
        catch (ChannelClosedException)
        {
            _logger.LogDebug("Message queue reader {Consumer} stopped because delivery intake is closed", consumer);
        }
    }
}
