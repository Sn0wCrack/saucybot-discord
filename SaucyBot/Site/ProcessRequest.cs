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
        SocketSlashCommand? Command = null)
        : this(match, guildConfiguration, CreateContext(message, Command))
    {
    }

    public Match Match { get; }
    public GuildConfiguration? GuildConfiguration { get; }
    public ProcessingContext? Context { get; }

    public SocketUserMessage? Message => (Context?.Message as LiveMessageContext)?.SocketMessage;

    public bool IsSlashCommand => Context?.Command is not null;

    public bool IsMessage => Context?.Message is not null;

    public string? UserLocale => Context?.Command?.UserLocale;

    public ulong? GuildId => Context?.Message?.GuildId ?? Context?.Command?.GuildId;

    // A live socket guild is retained only for existing in-process callers.
    public SocketGuild? Guild => Context?.Message is LiveMessageContext message
        ? message.Guild
        : Context?.Command is LiveCommandContext command
            ? command.Guild
            : null;

    private static ProcessingContext? CreateContext(SocketUserMessage? message, SocketSlashCommand? command)
    {
        if (message is not null)
        {
            return new ProcessingContext(
                CancellationToken.None,
                NsfwAllowed: true,
                Message: new LiveMessageContext(message));
        }

        return command is null
            ? null
            : new ProcessingContext(
                CancellationToken.None,
                NsfwAllowed: true,
                Command: new LiveCommandContext(command));
    }

    private class LiveProcessingContext
    {
        protected LiveProcessingContext(SocketGuild? guild) => Guild = guild;

        public SocketGuild? Guild { get; }
    }

    private sealed class LiveMessageContext : LiveProcessingContext, IMessageContext
    {
        private readonly SocketUserMessage _message;

        public LiveMessageContext(SocketUserMessage message)
            : base((message.Channel as SocketGuildChannel)?.Guild)
        {
            _message = message;
        }

        public ulong Id => _message.Id;
        public SocketUserMessage SocketMessage => _message;
        public ulong ChannelId => _message.Channel.Id;
        public ulong? GuildId => Guild?.Id;
        public string Content => _message.Content ?? "";
        public string AllMessageContent => _message.AllMessageContent();
        public ulong AuthorId => _message.Author.Id;
        public IReadOnlyCollection<ulong> AuthorRoleIds => (_message.Author as SocketGuildUser)?.Roles.Select(x => x.Id).ToArray() ?? [];
        public bool CanCreateEmbed => _message.Channel switch
        {
            SocketThreadChannel threadChannel => threadChannel.Guild.CurrentUser.GetPermissions(threadChannel).Has(Library.Constants.RequiredThreadPermissions),
            SocketGuildChannel guildChannel => guildChannel.Guild.CurrentUser.GetPermissions(guildChannel).Has(Library.Constants.RequiredChannelPermissions),
            _ => false,
        };
        public bool CanManageMessages => _message.Channel switch
        {
            SocketThreadChannel threadChannel => threadChannel.Guild.CurrentUser.GetPermissions(threadChannel).Has(ChannelPermission.ManageMessages),
            SocketGuildChannel guildChannel => guildChannel.Guild.CurrentUser.GetPermissions(guildChannel).Has(ChannelPermission.ManageMessages),
            _ => false,
        };
        public IReadOnlyList<Embed> CurrentEmbeds => _message.Embeds.ToArray();

        public Task<IReadOnlyList<Embed>> GetLatestEmbedsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Embed>>(CurrentEmbeds);
        }
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
