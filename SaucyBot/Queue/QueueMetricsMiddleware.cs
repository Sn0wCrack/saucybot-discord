using System.Diagnostics;
using SaucyBot.Diagnostics;

namespace SaucyBot.Queue;

/// <summary>
/// Records common handler outcomes and processing time for every work type.
/// Tags are limited to the bounded work type and outcome values; delivery,
/// message, and lease identifiers are never used as metric tags.
/// </summary>
public sealed class QueueMetricsMiddleware<T> : IQueueMiddleware<T>
{
    private readonly ISaucyBotMetrics _metrics;
    private readonly string _workType = WorkTypeFor(typeof(T));

    public QueueMetricsMiddleware(ISaucyBotMetrics metrics)
    {
        _metrics = metrics;
    }

    public async Task InvokeAsync(
        QueueWorkContext<T> context,
        QueueWorkDelegate<T> next,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            await next(context, cancellationToken);
            Record("succeeded", startedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Record("cancelled", startedAt);
            throw;
        }
        catch (Exception)
        {
            Record("failed", startedAt);
            throw;
        }
    }

    private void Record(string outcome, long startedAt)
    {
        _metrics.HandlerDuration.Record(
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            QueueMetricTags.Handler(_workType));
        _metrics.HandlerOutcomes.Add(1, QueueMetricTags.HandlerOutcome(_workType, outcome));
    }

    private static string WorkTypeFor(Type type)
    {
        if (type == typeof(MessageWorkItem))
        {
            return "message";
        }

        if (type == typeof(IInteractionWorkItem))
        {
            return "interaction";
        }

        return type.Name;
    }
}
