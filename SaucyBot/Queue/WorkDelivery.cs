namespace SaucyBot.Queue;

public sealed record WorkDelivery<T>(T Item, string DeliveryId, int Attempt, DateTimeOffset ReceivedAt, IWorkItemLease Lease);
