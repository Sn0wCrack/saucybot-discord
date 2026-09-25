namespace SaucyBot.Queue;

/// <summary>
/// Runs registered middleware in registration order around a named terminal
/// method. Each middleware receives a single-use next delegate so it can wrap
/// exactly one lifecycle step.
/// </summary>
public sealed class QueueMiddlewarePipeline<T> : IQueueMiddlewarePipeline<T>
{
    private readonly IQueueMiddleware<T>[] _middleware;

    public QueueMiddlewarePipeline(IEnumerable<IQueueMiddleware<T>> middleware)
    {
        _middleware = middleware.ToArray();
    }

    public Task InvokeAsync(
        QueueWorkContext<T> context,
        QueueWorkDelegate<T> terminal,
        CancellationToken cancellationToken) =>
        InvokeStepAsync(0, context, terminal, cancellationToken);

    private Task InvokeStepAsync(
        int index,
        QueueWorkContext<T> context,
        QueueWorkDelegate<T> terminal,
        CancellationToken cancellationToken)
    {
        if (index >= _middleware.Length)
        {
            return terminal(context, cancellationToken);
        }

        // A method group, not a closure over loop state, carries the rest of
        // the chain so middleware cannot invoke it more than once.
        var next = new SingleUseNext((nextContext, nextToken) =>
            InvokeStepAsync(index + 1, nextContext, terminal, nextToken));

        return _middleware[index].InvokeAsync(context, next.InvokeAsync, cancellationToken);
    }

    private sealed class SingleUseNext(QueueWorkDelegate<T> next)
    {
        private int _called;

        public Task InvokeAsync(QueueWorkContext<T> context, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _called, 1) != 0)
            {
                throw new InvalidOperationException("Queue middleware called next more than once.");
            }

            return next(context, cancellationToken);
        }
    }
}
