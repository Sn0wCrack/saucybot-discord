namespace SaucyBot.Queue;

public sealed record RedisStreamEntry(string EntryId, string Payload);
