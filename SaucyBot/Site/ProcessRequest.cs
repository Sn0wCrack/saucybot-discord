using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Database.Models;
using SaucyBot.Extensions.Discord;

namespace SaucyBot.Site;

public sealed record ProcessRequest
{
    public ProcessRequest(
        Match match,
        GuildConfiguration? guildConfiguration = null,
        ProcessingContext? Context = null)
    {
        Match = match;
        GuildConfiguration = guildConfiguration;
        this.Context = Context;
    }

    // Compatibility constructor for live gateway callers. Socket objects are adapted in-process and are not part of the request contract.
    public ProcessRequest(
        Match match,
        GuildConfiguration? guildConfiguration,
        SocketUserMessage? message = null,
        SocketSlashCommand? Command = null,
        bool nsfwAllowed = true,
        CancellationToken cancellationToken = default)
        : this(match, guildConfiguration, CreateContext(message, Command, nsfwAllowed, cancellationToken))
    {
    }

    public Match Match { get; }
    public GuildConfiguration? GuildConfiguration { get; }
    public ProcessingContext? Context { get; }

    public SocketUserMessage? Message => (Context?.Message as DiscordMessageContext)?.SocketMessage;

    public SocketSlashCommand? Command => (Context?.Command as LiveCommandContext)?.SocketCommand;

    public bool IsSlashCommand => Context?.Command is not null;

    public bool IsMessage => Context?.Message is not null;

    public string? UserLocale => Context?.Command?.UserLocale;

    public ulong? GuildId => Context?.Message?.GuildId ?? Context?.Command?.GuildId;

    // A live socket guild is retained only for existing in-process callers.
    public SocketGuild? Guild => Context?.Message is DiscordMessageContext message
        ? (message.SocketMessage.Channel as SocketGuildChannel)?.Guild
        : Context?.Command is LiveCommandContext command
            ? command.Guild
            : null;

    private static ProcessingContext? CreateContext(
        SocketUserMessage? message,
        SocketSlashCommand? command,
        bool nsfwAllowed,
        CancellationToken cancellationToken)
    {
        return message is null && command is null
            ? null
            : new ProcessingContext(
                cancellationToken,
                NsfwAllowed: nsfwAllowed,
                Message: message is null ? null : new DiscordMessageContext(message),
                Command: command is null ? null : new LiveCommandContext(command));
    }

    private class LiveProcessingContext
    {
        protected LiveProcessingContext(SocketGuild? guild) => Guild = guild;

        public SocketGuild? Guild { get; }
    }

    private sealed class LiveCommandContext : LiveProcessingContext, ICommandContext
    {
        private readonly SocketSlashCommand _command;

        public LiveCommandContext(SocketSlashCommand command)
            : base((command.Channel as SocketGuildChannel)?.Guild)
        {
            _command = command;
        }

        public ulong Id => _command.Id;
        public SocketSlashCommand SocketCommand => _command;
        public ulong ChannelId => _command.Channel.Id;
        public ulong? GuildId => Guild?.Id;
        public ulong UserId => _command.User.Id;
        public IReadOnlyCollection<ulong> UserRoleIds => (_command.User as SocketGuildUser)?.Roles.Select(x => x.Id).ToArray() ?? [];
        public string? OptionContent => (string?)_command.Data.Options.FirstOrDefault()?.Value;
        public string? UserLocale => _command.UserLocale;
        public bool CanCreateEmbed => _command.Channel switch
        {
            SocketDMChannel or SocketGroupChannel => true,
            SocketThreadChannel threadChannel => threadChannel.Guild.CurrentUser.GetPermissions(threadChannel).Has(Library.Constants.RequiredThreadPermissions),
            SocketGuildChannel guildChannel => guildChannel.Guild.CurrentUser.GetPermissions(guildChannel).Has(Library.Constants.RequiredChannelPermissions),
            _ => false,
        };

        public async Task FollowupAsync(string content, bool ephemeral = false, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _command.FollowupAsync(content, ephemeral: ephemeral);
        }
    }
}
