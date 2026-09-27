namespace SaucyBot.Queue;

public interface IWorkItemProducer<T>
{
    Task<EnqueueResult> EnqueueAsync(T item, TimeSpan timeout, CancellationToken cancellationToken);
}

public enum EnqueueResult
{
    Accepted,
    TimedOut,
}
