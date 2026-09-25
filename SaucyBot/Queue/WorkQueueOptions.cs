namespace SaucyBot.Queue;

public sealed record WorkQueueOptions
{
    public QueueDriverType Driver { get; init; } = QueueDriverType.Redis;
    public int MessageWorkerCount { get; init; } = 5;
    public int InteractionWorkerCount { get; init; } = 5;
    public int InteractionChannelCapacity { get; init; } = 100;
    public int RecoveryHandoffCapacity { get; init; } = 25;
    public int MaxProcessingAttempts { get; init; } = 3;
    public TimeSpan EnqueueTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan BackendOperationTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxProcessingTime { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan PendingMessageIdleTime { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReclaimerInterval { get; init; } = TimeSpan.FromSeconds(5);
    public bool ClearPendingOnStartup { get; init; }
    public TimeSpan ShutdownDrainTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public enum QueueDriverType
{
    Redis,
}
