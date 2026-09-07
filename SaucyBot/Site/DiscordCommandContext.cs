using Discord;
using Discord.WebSocket;
using SaucyBot.Extensions.Discord;

namespace SaucyBot.Site;

public sealed class DiscordCommandContext : ICommandContext
{
    private readonly SocketSlashCommand _command;
    private readonly SocketGuild? _guild;

    public ulong Id => _command.Id;
    public ulong ChannelId => _command.Channel.Id;
    public ulong? GuildId => _guild?.Id;
    public string? GuildPreferredLocale => _guild?.PreferredLocale;
    public bool IsGuildCommunity => _guild?.Features.HasFeature(GuildFeature.Community) ?? false;
    public ulong UserId => _command.User.Id;
    public IReadOnlyCollection<ulong> UserRoleIds => (_command.User as SocketGuildUser)?.Roles.Select(x => x.Id).ToArray() ?? [];
    public string? OptionContent => (string?)_command.Data.Options.FirstOrDefault()?.Value;
    public string? UserLocale => _command.UserLocale;
    public bool CanCreateEmbed => _command.Channel.CanCreateEmbed(allowDirectMessages: true);

    public DiscordCommandContext(SocketSlashCommand command)
    {
        _command = command;
        _guild = (command.Channel as SocketGuildChannel)?.Guild;
    }

    public async Task FollowupAsync(string content, bool ephemeral = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _command.FollowupAsync(content, ephemeral: ephemeral);
    }
}
