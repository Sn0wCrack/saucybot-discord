using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SaucyBot.Queue;

public static class WorkQueueOptionsLoader
{
    private const string QueueSectionName = "Queue";
    private const string LegacySectionName = "Redis";

    public static WorkQueueOptions Bind(IConfiguration configuration, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        var queueSection = configuration.GetSection(QueueSectionName);
        var legacySection = queueSection.GetSection(LegacySectionName);
        var options = queueSection.Get<WorkQueueOptions>() ?? new WorkQueueOptions();

        options = MapLegacyTiming(
            queueSection,
            legacySection,
            options,
            nameof(WorkQueueOptions.HeartbeatInterval),
            logger,
            static (current, value) => current with { HeartbeatInterval = value });
        options = MapLegacyTiming(
            queueSection,
            legacySection,
            options,
            nameof(WorkQueueOptions.ReclaimerInterval),
            logger,
            static (current, value) => current with { ReclaimerInterval = value });
        options = MapLegacyTiming(
            queueSection,
            legacySection,
            options,
            nameof(WorkQueueOptions.PendingMessageIdleTime),
            logger,
            static (current, value) => current with { PendingMessageIdleTime = value });

        return options;
    }

    private static WorkQueueOptions MapLegacyTiming(
        IConfigurationSection genericSection,
        IConfigurationSection legacySection,
        WorkQueueOptions options,
        string key,
        ILogger logger,
        Func<WorkQueueOptions, TimeSpan, WorkQueueOptions> apply)
    {
        if (genericSection.GetSection(key).Exists())
        {
            return options;
        }

        var legacyValue = legacySection[key];
        if (string.IsNullOrWhiteSpace(legacyValue))
        {
            return options;
        }

        logger.LogWarning(
            "Queue:Redis:{Key} is deprecated and will be removed. Move this value to Queue:{Key}. The legacy value is still applied for now.",
            key,
            key);

        return apply(options, TimeSpan.Parse(legacyValue, CultureInfo.InvariantCulture));
    }
}
