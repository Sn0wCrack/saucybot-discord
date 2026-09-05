using Discord;
using SaucyBot.Common;
using SaucyBot.Library.Discord;
using SaucyBot.Queue;

namespace SaucyBot.Site;

public sealed class QueuedMessageContext : IMessageContext
{
    private readonly MessageWorkItem _item;
    private readonly IMessageResolver _resolver;
    private IUserMessage? _resolvedMessage;
    private bool _attemptedResolution;

    public QueuedMessageContext(MessageWorkItem item, IMessageResolver resolver)
    {
        _item = item;
        _resolver = resolver;
        CurrentEmbeds = item.Embeds.Select(embed => new EmbedBuilder
        {
            Title = embed.Title,
            Description = embed.Description,
            Url = embed.Url,
        }.Build()).ToArray();
    }

    public ulong Id => _item.MessageId;
    public ulong ChannelId => _item.ChannelId;
    public ulong? GuildId
    {
        get
        {
            if (_item.GuildId == 0)
            {
                return null;
            }

            return _item.GuildId;
        }
    }
    public string Content => _item.Content;
    public string AllMessageContent => string.IsNullOrEmpty(_item.ForwardedContent)
        ? _item.Content
        : $"{_item.Content}\n{_item.ForwardedContent}";
    public string CleanContent => Helper.MarkdownToPlainText(AllMessageContent).Trim();
    public ulong AuthorId => _item.AuthorId;
    public IReadOnlyCollection<ulong> AuthorRoleIds => _item.AuthorRoleIds;
    public bool CanCreateEmbed => _item.CanCreateEmbed;
    public bool CanManageMessages => _item.CanManageMessages;
    public bool IsNsfw => _resolver.IsNsfw(ChannelId);
    public IReadOnlyList<Embed> CurrentEmbeds { get; private set; }

    public async Task<IReadOnlyList<Embed>> GetLatestEmbedsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (CurrentEmbeds.Count != 0)
        {
            return CurrentEmbeds;
        }

        var message = await ResolveMessageAsync(cancellationToken);
        if (message is not null)
        {
            CurrentEmbeds = message.Embeds.OfType<Embed>().ToArray();
        }

        return CurrentEmbeds;
    }

    public async Task<IUserMessage?> ResolveMessageAsync(CancellationToken cancellationToken)
    {
        if (_resolvedMessage is not null)
        {
            return _resolvedMessage;
        }

        if (_attemptedResolution)
        {
            return null;
        }

        _resolvedMessage = _resolver.GetCachedMessage(ChannelId, Id);
        if (_resolvedMessage is not null)
        {
            return _resolvedMessage;
        }

        _attemptedResolution = true;
        return _resolvedMessage = await _resolver.FetchMessageAsync(ChannelId, Id, cancellationToken);
    }
}
