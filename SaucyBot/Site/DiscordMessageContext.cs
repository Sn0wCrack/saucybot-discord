using Discord;
using Discord.WebSocket;
using SaucyBot.Extensions.Discord;

namespace SaucyBot.Site;

public interface IMessageResolver
{
    IUserMessage? GetCachedMessage(ulong channelId, ulong messageId);
    Task<IUserMessage?> FetchMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken);
    bool IsNsfw(ulong channelId);
}

public sealed class DiscordMessageResolver : IMessageResolver
{
    private BaseSocketClient? _client;

    public void Initialize(BaseSocketClient client) => _client = client;

    public IUserMessage? GetCachedMessage(ulong channelId, ulong messageId)
    {
        return (_client?.GetChannel(channelId) as ISocketMessageChannel)?.GetCachedMessage(messageId) as IUserMessage;
    }

    public async Task<IUserMessage?> FetchMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var channel = _client?.GetChannel(channelId) as IMessageChannel;
        return channel is null ? null : await channel.GetMessageAsync(messageId) as IUserMessage;
    }

    public bool IsNsfw(ulong channelId)
    {
        return _client?.GetChannel(channelId) switch
        {
            SocketThreadChannel { ParentChannel: ITextChannel parent } => parent.IsNsfw,
            ITextChannel channel => channel.IsNsfw,
            _ => false,
        };
    }
}

public sealed class DiscordMessageContext : IMessageContext
{
    private readonly SocketUserMessage _message;
    private readonly IMessageResolver? _resolver;
    private readonly IReadOnlyList<Embed>? _initialEmbeds;
    private IUserMessage? _resolvedMessage;
    private bool _attemptedResolution;

    public DiscordMessageContext(
        SocketUserMessage message,
        IMessageResolver? resolver = null,
        IReadOnlyList<Embed>? initialEmbeds = null)
    {
        _message = message;
        _resolver = resolver;
        _initialEmbeds = initialEmbeds;
    }

    public SocketUserMessage SocketMessage => _message;
    public ulong Id => _message.Id;
    public ulong ChannelId => _message.Channel?.Id ?? 0;
    public ulong? GuildId => (_message.Channel as SocketGuildChannel)?.Guild.Id;
    public string Content => _message.Content ?? "";
    public string AllMessageContent => _message.AllMessageContent();
    public string CleanContent => _message.AllMessageCleanContent();
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
    public bool IsNsfw => _message.Channel switch
    {
        SocketThreadChannel { ParentChannel: ITextChannel parent } => parent.IsNsfw,
        ITextChannel channel => channel.IsNsfw,
        _ => false,
    };
    public IReadOnlyList<Embed> CurrentEmbeds => _message.Embeds?.ToArray() ?? _initialEmbeds ?? [];

    public Task<IReadOnlyList<Embed>> GetLatestEmbedsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GetLatestEmbedsCoreAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Embed>> GetLatestEmbedsCoreAsync(CancellationToken cancellationToken)
    {
        if (_resolver is not null)
        {
            var cached = _resolver.GetCachedMessage(ChannelId, Id);
            if (cached is not null)
            {
                _resolvedMessage = cached;
                return cached.Embeds.OfType<Embed>().ToArray();
            }

            if (CurrentEmbeds.Count == 0)
            {
                var message = await ResolveWithResolverAsync(cancellationToken);
                if (message is not null)
                {
                    return message.Embeds.OfType<Embed>().ToArray();
                }
            }
        }

        return CurrentEmbeds;
    }

    public async Task<IUserMessage?> ResolveMessageAsync(CancellationToken cancellationToken)
    {
        if (_resolver is null)
        {
            return _message;
        }

        return await ResolveWithResolverAsync(cancellationToken);
    }

    private async Task<IUserMessage?> ResolveWithResolverAsync(CancellationToken cancellationToken)
    {
        if (_resolvedMessage is not null || _attemptedResolution)
        {
            return _resolvedMessage;
        }

        _resolvedMessage = _resolver!.GetCachedMessage(ChannelId, Id);
        if (_resolvedMessage is null)
        {
            _attemptedResolution = true;
            _resolvedMessage = await _resolver.FetchMessageAsync(ChannelId, Id, cancellationToken);
        }

        return _resolvedMessage;
    }
}
