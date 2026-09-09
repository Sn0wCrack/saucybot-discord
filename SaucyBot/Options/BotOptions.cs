using Discord;
using SaucyBot.Library;

namespace SaucyBot.Options;

public sealed class BotOptions
{
    public string ShardMode { get; init; } = "Automatic";
    public string? DiscordToken { get; init; }
    public int? ShardId { get; init; }
    public int? TotalShards { get; init; }
    public int? MessageCacheSize { get; init; }
    public bool RestrictNSFW { get; init; }
    public uint MaximumEmbeds { get; init; } = Constants.DefaultMaximumEmbeds;
    public string[] DisabledSites { get; init; } = [];

    public DiscordStatusOptions Status { get; init; } = new();
}

public sealed class DiscordStatusOptions
{
    public bool Enabled { get; init; }
    public ActivityType? Type { get; init; }
    public string? Text { get; init; }
}
