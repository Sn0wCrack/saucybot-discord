using Discord;

namespace SaucyBot.Site;

public interface IMessageContext
{
    ulong Id { get; }
    DateTimeOffset? EnqueuedAt { get; }
    ulong ChannelId { get; }
    ulong? GuildId { get; }
    string Content { get; }
    string AllMessageContent { get; }
    string CleanContent { get; }
    ulong AuthorId { get; }
    IReadOnlyCollection<ulong> AuthorRoleIds { get; }
    bool CanCreateEmbed { get; }
    bool CanManageMessages { get; }
    bool IsNsfw { get; }
    IReadOnlyList<Embed> CurrentEmbeds { get; }

    Task<IReadOnlyList<Embed>> GetLatestEmbedsAsync(CancellationToken cancellationToken);
    Task<IUserMessage?> ResolveMessageAsync(CancellationToken cancellationToken);
}
