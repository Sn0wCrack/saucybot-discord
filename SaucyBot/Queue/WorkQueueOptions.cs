namespace SaucyBot.Queue;

public sealed class WorkQueueOptions
{
    public QueueDriverType Driver { get; set; } = QueueDriverType.Redis;
    public int MessageWorkerCount { get; set; } = 5;
    public int InteractionWorkerCount { get; set; } = 5;
    public int InteractionChannelCapacity { get; set; } = 100;
    public int RecoveryHandoffCapacity { get; set; } = 25;
    public int MaxProcessingAttempts { get; set; } = 3;
    public TimeSpan EnqueueTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan BackendOperationTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxProcessingTime { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan PendingMessageIdleTime { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReclaimerInterval { get; set; } = TimeSpan.FromSeconds(5);
    public bool ClearPendingOnStartup { get; set; }
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public enum QueueDriverType
{
    Redis,
}
