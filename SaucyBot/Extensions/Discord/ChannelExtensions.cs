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
            SocketNewsChannel => false,
            SocketThreadChannel threadChannel =>
                threadChannel.Guild.CurrentUser.GetPermissions(threadChannel)
                    .Has(Constants.RequiredThreadChannelPermissions),
            SocketTextChannel textChannel =>
                textChannel.Guild.CurrentUser.GetPermissions(textChannel)
                    .Has(Constants.RequiredTextChannelPermissions),
            _ => false,
        };
    }
}
