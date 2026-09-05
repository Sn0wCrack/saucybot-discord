namespace SaucyBot.Site;

public interface ICommandContext
{
    ulong Id { get; }
    ulong ChannelId { get; }
    ulong? GuildId { get; }
    ulong UserId { get; }
    IReadOnlyCollection<ulong> UserRoleIds { get; }
    string? OptionContent { get; }
    string? UserLocale { get; }
    bool CanCreateEmbed { get; }

    Task FollowupAsync(
        string content,
        bool ephemeral = false,
        CancellationToken cancellationToken = default);
}
