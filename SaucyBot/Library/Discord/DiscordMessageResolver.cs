using Discord;
using Discord.WebSocket;

namespace SaucyBot.Library.Discord;

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
        if (channel is null)
        {
            return null;
        }

        return await channel.GetMessageAsync(messageId) as IUserMessage;
    }

    public bool IsNsfw(ulong channelId)
    {
        var channel = _client?.GetChannel(channelId);
        if (channel is SocketThreadChannel { ParentChannel: ITextChannel parent })
        {
            return parent.IsNsfw;
        }

        return channel is ITextChannel textChannel && textChannel.IsNsfw;
    }
}
