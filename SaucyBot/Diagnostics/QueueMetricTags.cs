using System.Diagnostics;

namespace SaucyBot.Diagnostics;

public static class QueueMetricTags
{
    public static TagList Lease(string consumer) => new()
    {
        { "work_type", "message" },
        { "consumer_type", consumer.EndsWith("-recovery", StringComparison.Ordinal) ? "recovery" : "normal" },
    };
}
