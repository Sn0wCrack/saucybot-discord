using Microsoft.Extensions.Options;

namespace SaucyBot.Queue.Redis;

public static class RedisWorkQueueOptionsValidator
{
    public static void Validate(RedisWorkQueueOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        RequirePositive(failures, options.MalformedCleanupMaxAttempts, nameof(options.MalformedCleanupMaxAttempts));
        RequirePositive(failures, options.RetryDelay, nameof(options.RetryDelay));
        RequirePositive(failures, options.PendingReadTimeout, nameof(options.PendingReadTimeout));
        RequirePositive(failures, options.MalformedCleanupMaxDelay, nameof(options.MalformedCleanupMaxDelay));

        if (failures.Count > 0)
        {
            throw new OptionsValidationException(
                nameof(RedisWorkQueueOptions),
                typeof(RedisWorkQueueOptions),
                failures);
        }
    }

    private static void RequirePositive(ICollection<string> failures, int value, string name)
    {
        if (value <= 0)
        {
            failures.Add($"{name} must be greater than zero but was {value}.");
        }
    }

    private static void RequirePositive(ICollection<string> failures, TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            failures.Add($"{name} must be greater than zero but was {value}.");
        }
    }
}
