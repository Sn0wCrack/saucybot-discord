using System.Diagnostics;

namespace SaucyBot.Diagnostics;

public static class QueueTelemetry
{
    public const string ActivitySourceName = "SaucyBot.Queue";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
