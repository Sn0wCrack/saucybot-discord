namespace SaucyBot.Queue.Redis;

public sealed record RedisStreamEntry(string EntryId, string Payload, int DeliveryCount = 1);
