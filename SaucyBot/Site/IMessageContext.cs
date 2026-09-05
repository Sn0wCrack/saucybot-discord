using Discord;

namespace SaucyBot.Site;

public interface IMessageContext
{
    ulong Id { get; }
    ulong ChannelId { get; }
    ulong? GuildId { get; }
    string Content { get; }
    string AllMessageContent { get; }
    ulong AuthorId { get; }
    IReadOnlyCollection<ulong> AuthorRoleIds { get; }
    bool CanCreateEmbed { get; }
    bool CanManageMessages { get; }
    IReadOnlyList<Embed> CurrentEmbeds { get; }

    Task<IReadOnlyList<Embed>> GetLatestEmbedsAsync(CancellationToken cancellationToken);
}
