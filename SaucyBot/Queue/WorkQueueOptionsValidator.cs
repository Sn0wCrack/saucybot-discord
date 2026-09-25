using Microsoft.Extensions.Options;

namespace SaucyBot.Queue;

public static class WorkQueueOptionsValidator
{
    public static void Validate(WorkQueueOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        RequirePositive(failures, options.MessageWorkerCount, nameof(options.MessageWorkerCount));
        RequirePositive(failures, options.InteractionWorkerCount, nameof(options.InteractionWorkerCount));
        RequirePositive(failures, options.InteractionChannelCapacity, nameof(options.InteractionChannelCapacity));
        RequirePositive(failures, options.RecoveryHandoffCapacity, nameof(options.RecoveryHandoffCapacity));
        RequirePositive(failures, options.MaxProcessingAttempts, nameof(options.MaxProcessingAttempts));

        RequirePositive(failures, options.EnqueueTimeout, nameof(options.EnqueueTimeout));
        RequirePositive(failures, options.BackendOperationTimeout, nameof(options.BackendOperationTimeout));
        RequirePositive(failures, options.MaxProcessingTime, nameof(options.MaxProcessingTime));
        RequirePositive(failures, options.HeartbeatInterval, nameof(options.HeartbeatInterval));
        RequirePositive(failures, options.PendingMessageIdleTime, nameof(options.PendingMessageIdleTime));
        RequirePositive(failures, options.ReclaimerInterval, nameof(options.ReclaimerInterval));
        RequirePositive(failures, options.ShutdownDrainTimeout, nameof(options.ShutdownDrainTimeout));

        if (options.HeartbeatInterval >= options.PendingMessageIdleTime)
        {
            failures.Add(
                $"Queue:HeartbeatInterval ({options.HeartbeatInterval}) must be shorter than Queue:PendingMessageIdleTime ({options.PendingMessageIdleTime}).");
        }

        if (failures.Count > 0)
        {
            throw new OptionsValidationException(
                nameof(WorkQueueOptions),
                typeof(WorkQueueOptions),
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
