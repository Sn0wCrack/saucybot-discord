using System.Diagnostics;

namespace SaucyBot.Diagnostics;

public static class QueueMetricTags
{
    public static TagList Lease(string consumer) => new()
    {
        { "work_type", "message" },
        { "consumer_type", consumer.EndsWith("-recovery", StringComparison.Ordinal) ? "recovery" : "normal" },
    };

    public static TagList Handler(string workType) => new()
    {
        { "work_type", workType },
    };

    public static TagList HandlerOutcome(string workType, string outcome) => new()
    {
        { "work_type", workType },
        { "outcome", outcome },
    };

    public static TagList BackendOperation(string operation) => new()
    {
        { "operation", operation },
    };
}
