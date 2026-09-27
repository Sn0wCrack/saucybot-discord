namespace SaucyBot.Queue;

public sealed record QueueWorkContext<T>(
    T Item,
    string DeliveryId,
    int Attempt,
    DateTimeOffset ReceivedAt);

public delegate Task QueueWorkDelegate<T>(
    QueueWorkContext<T> context,
    CancellationToken cancellationToken);
