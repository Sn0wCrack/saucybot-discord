namespace SaucyBot.Queue;

public sealed class WorkQueueOptions
{
    public string ConnectionString { get; set; } = "queue:6379";
    public string StreamName { get; set; } = "saucybot:messages";
    public string ConsumerGroup { get; set; } = "saucybot-workers";
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan PendingClaimIdleTime { get; set; } = TimeSpan.FromMinutes(1);
    public bool ClearPendingOnStartup { get; set; }
    public int MessageWorkerCount { get; set; } = 5;
    public int InteractionWorkerCount { get; set; } = 5;
    public int InteractionChannelCapacity { get; set; } = 100;
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
