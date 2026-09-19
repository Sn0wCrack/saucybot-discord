namespace SaucyBot.Queue;

public interface IMessageWorkQueue
{
    Task EnqueueAsync(MessageWorkItem item, CancellationToken cancellationToken);

    IAsyncEnumerable<QueuedMessageWorkItem> ReadAsync(string consumer, CancellationToken cancellationToken);

    Task StartAsync(CancellationToken cancellationToken);

    Task CompleteAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken);

    Task<WorkItemFailureResult> FailAsync(
        QueuedMessageWorkItem item,
        Exception exception,
        CancellationToken cancellationToken);
}

public enum WorkItemFailureAction
{
    Retried,
    Discarded,
}

public sealed record WorkItemFailureResult(
    WorkItemFailureAction Action,
    int Attempt);
