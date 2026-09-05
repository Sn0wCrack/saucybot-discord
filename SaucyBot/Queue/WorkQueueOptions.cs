namespace SaucyBot.Queue;

public sealed record WorkQueueOptions
{
    public string ConnectionString { get; init; } = "queue:6379";
    public string StreamName { get; init; } = "saucybot:messages";
    public string ConsumerGroup { get; init; } = "saucybot-workers";
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public bool ClearPendingOnStartup { get; init; }
    public int MessageWorkerCount { get; init; } = 5;
    public int InteractionWorkerCount { get; init; } = 5;
    public int InteractionChannelCapacity { get; init; } = 100;
    public TimeSpan ShutdownDrainTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
