namespace SaucyBot.Queue;

public interface IMessageWorkQueue
{
    Task EnqueueAsync(MessageWorkItem item, CancellationToken cancellationToken);

    IAsyncEnumerable<QueuedMessageWorkItem> ReadAsync(string consumer, CancellationToken cancellationToken);

    Task AcknowledgeAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken);

    Task ClearPendingAsync(CancellationToken cancellationToken);
}
