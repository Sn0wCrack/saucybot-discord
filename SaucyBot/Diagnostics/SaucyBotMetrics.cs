using System.Diagnostics.Metrics;

namespace SaucyBot.Diagnostics;

public sealed class SaucyBotMetrics : IDisposable
{
    public const string MeterName = "SaucyBot";

    private readonly Meter _meter = new(MeterName);

    public SaucyBotMetrics()
    {
        QueueDepth = _meter.CreateUpDownCounter<long>("saucybot.queue.depth", "items");
        QueueAge = _meter.CreateHistogram<double>("saucybot.queue.age", "ms");
        ActiveWorkers = _meter.CreateUpDownCounter<long>("saucybot.workers.active", "workers");
        ProcessingDuration = _meter.CreateHistogram<double>("saucybot.processing.duration", "ms");
        Enqueued = _meter.CreateCounter<long>("saucybot.queue.enqueued", "items");
        Dequeued = _meter.CreateCounter<long>("saucybot.queue.dequeued", "items");
        Failed = _meter.CreateCounter<long>("saucybot.queue.failed", "items");
        DownloadBytes = _meter.CreateCounter<long>("saucybot.download.bytes", "By");
        DownloadConcurrency = _meter.CreateUpDownCounter<long>("saucybot.download.concurrency", "downloads");
    }

    public UpDownCounter<long> QueueDepth { get; }

    public Histogram<double> QueueAge { get; }

    public UpDownCounter<long> ActiveWorkers { get; }

    public Histogram<double> ProcessingDuration { get; }

    public Counter<long> Enqueued { get; }

    public Counter<long> Dequeued { get; }

    public Counter<long> Failed { get; }

    public Counter<long> DownloadBytes { get; }

    public UpDownCounter<long> DownloadConcurrency { get; }

    public void Dispose() => _meter.Dispose();
}
