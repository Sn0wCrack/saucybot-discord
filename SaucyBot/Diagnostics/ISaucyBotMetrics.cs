using System.Diagnostics.Metrics;

namespace SaucyBot.Diagnostics;

public interface ISaucyBotMetrics : IDisposable
{
    UpDownCounter<long> QueueDepth { get; }
    Histogram<double> QueueAge { get; }
    UpDownCounter<long> ActiveWorkers { get; }
    Counter<long> Enqueued { get; }
    Counter<long> EnqueueTimedOut { get; }
    Counter<long> Dequeued { get; }
    Counter<long> Failed { get; }
    Counter<long> Succeeded { get; }
    Counter<long> Retried { get; }
    Counter<long> Cancelled { get; }
    Counter<long> HandlerOutcomes { get; }
    Histogram<double> HandlerDuration { get; }
    Counter<long> Malformed { get; }
    Counter<long> CleanupFailed { get; }
    Counter<long> LeaseRenewed { get; }
    Counter<long> LeaseLost { get; }
    Counter<long> Reclaimed { get; }
    Counter<long> WorkerRestarts { get; }
    Counter<long> DownloadBytes { get; }
    UpDownCounter<long> DownloadConcurrency { get; }
}
