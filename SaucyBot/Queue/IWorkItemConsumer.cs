namespace SaucyBot.Queue;

public interface IWorkItemConsumer<T>
{
    IAsyncEnumerable<WorkDelivery<T>> ReadAsync(string consumer, CancellationToken cancellationToken);

    IAsyncEnumerable<WorkDelivery<T>> RecoverAsync(
        string consumer,
        TimeSpan minimumIdleTime,
        int count,
        CancellationToken cancellationToken);

    Task StartAsync(CancellationToken cancellationToken);
}
