using System.Diagnostics.Metrics;

namespace SaucyBot.Diagnostics;

public sealed class SaucyBotMetrics : ISaucyBotMetrics
{
    public const string MeterName = "SaucyBot";

    private readonly Meter _meter = new(MeterName);

    public SaucyBotMetrics()
    {
        QueueDepth = _meter.CreateUpDownCounter<long>("saucybot.queue.depth", "items");
        QueueAge = _meter.CreateHistogram<double>("saucybot.queue.age", "ms");
        ActiveWorkers = _meter.CreateUpDownCounter<long>("saucybot.workers.active", "workers");
        Enqueued = _meter.CreateCounter<long>("saucybot.queue.enqueued", "items");
        EnqueueTimedOut = _meter.CreateCounter<long>("saucybot.queue.enqueue_timed_out", "items");
        Dequeued = _meter.CreateCounter<long>("saucybot.queue.dequeued", "items");
        Failed = _meter.CreateCounter<long>("saucybot.queue.failed", "items");
        Succeeded = _meter.CreateCounter<long>("saucybot.queue.succeeded", "items");
        Retried = _meter.CreateCounter<long>("saucybot.queue.retried", "items");
        Cancelled = _meter.CreateCounter<long>("saucybot.queue.cancelled", "items");
        Malformed = _meter.CreateCounter<long>("saucybot.queue.malformed", "items");
        CleanupFailed = _meter.CreateCounter<long>("saucybot.queue.cleanup_failed", "items");
        LeaseRenewed = _meter.CreateCounter<long>("saucybot.queue.lease_renewed", "renewals");
        LeaseLost = _meter.CreateCounter<long>("saucybot.queue.lease_lost", "items");
        Reclaimed = _meter.CreateCounter<long>("saucybot.queue.reclaimed", "items");
        WorkerRestarts = _meter.CreateCounter<long>("saucybot.queue.worker_restarted", "restarts");
        DownloadBytes = _meter.CreateCounter<long>("saucybot.download.bytes", "By");
        DownloadConcurrency = _meter.CreateUpDownCounter<long>("saucybot.download.concurrency", "downloads");
    }

    public UpDownCounter<long> QueueDepth { get; }

    public Histogram<double> QueueAge { get; }

    public UpDownCounter<long> ActiveWorkers { get; }

    public Counter<long> Enqueued { get; }

    public Counter<long> EnqueueTimedOut { get; }

    public Counter<long> Dequeued { get; }

    public Counter<long> Failed { get; }

    public Counter<long> Succeeded { get; }

    public Counter<long> Retried { get; }

    public Counter<long> Cancelled { get; }

    public Counter<long> Malformed { get; }

    public Counter<long> CleanupFailed { get; }

    public Counter<long> LeaseRenewed { get; }

    public Counter<long> LeaseLost { get; }

    public Counter<long> Reclaimed { get; }

    public Counter<long> WorkerRestarts { get; }

    public Counter<long> DownloadBytes { get; }

    public UpDownCounter<long> DownloadConcurrency { get; }

    public void Dispose() => _meter.Dispose();
}
