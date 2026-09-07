using Discord;
using Discord.WebSocket;
using SaucyBot.Library;

namespace SaucyBot.Extensions.Discord;

public static class ChannelExtensions
{
    extension(ISocketMessageChannel channel)
    {
        public bool CanCreateEmbed(bool allowDirectMessages = false) => channel switch
        {
            SocketDMChannel or SocketGroupChannel => allowDirectMessages,
            SocketThreadChannel threadChannel =>
                threadChannel.Guild.CurrentUser.GetPermissions(threadChannel)
                    .Has(Constants.RequiredThreadPermissions),
            SocketGuildChannel guildChannel =>
                guildChannel.Guild.CurrentUser.GetPermissions(guildChannel)
                    .Has(Constants.RequiredChannelPermissions),
            _ => false,
        };
    }
}
