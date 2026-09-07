using Discord;
using Discord.WebSocket;

namespace SaucyBot.Library.Discord;

public interface IMessageResolver
{
    void Initialize(BaseSocketClient client);
    IUserMessage? GetCachedMessage(ulong channelId, ulong messageId);
    Task<IUserMessage?> FetchMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken);
    bool IsNsfw(ulong channelId);
}
