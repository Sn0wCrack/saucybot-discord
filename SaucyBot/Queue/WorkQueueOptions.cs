namespace SaucyBot.Queue;

public sealed record WorkQueueOptions
{
    public QueueDriverType Driver { get; init; } = QueueDriverType.Redis;
    public int MessageWorkerCount { get; init; } = 5;
    public int InteractionWorkerCount { get; init; } = 5;
    public int InteractionChannelCapacity { get; init; } = 100;
    public int MaxProcessingAttempts { get; init; } = 3;
    public TimeSpan MaxProcessingTime { get; init; } = TimeSpan.FromMinutes(5);
    public bool ClearPendingOnStartup { get; init; }
    public TimeSpan ShutdownDrainTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public RedisWorkQueueOptions Redis { get; init; } = new();
}

public enum QueueDriverType
{
    Redis,
}

public sealed record RedisWorkQueueOptions
{
    public string ConnectionString { get; init; } = "queue:6379";
    public string StreamName { get; init; } = "saucybot:messages";
    public string ConsumerGroup { get; init; } = "saucybot-workers";
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ReclaimerInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan PendingMessageIdleTime { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan PendingReadTimeout { get; init; } = TimeSpan.FromSeconds(1);
    public int MalformedCleanupMaxAttempts { get; init; } = 3;
    public TimeSpan MalformedCleanupMaxDelay { get; init; } = TimeSpan.FromSeconds(5);
}
