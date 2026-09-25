namespace SaucyBot.Queue;

public interface IQueueMiddleware<T>
{
    Task InvokeAsync(
        QueueWorkContext<T> context,
        QueueWorkDelegate<T> next,
        CancellationToken cancellationToken);
}

public interface IQueueMiddlewarePipeline<T>
{
    Task InvokeAsync(
        QueueWorkContext<T> context,
        QueueWorkDelegate<T> terminal,
        CancellationToken cancellationToken);
}
