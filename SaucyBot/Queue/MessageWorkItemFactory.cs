using Discord;
using Discord.WebSocket;

namespace SaucyBot.Queue;

public interface IMessageWorkItemFactory
{
    MessageWorkItem? Create(SocketMessage message);
}

public sealed class MessageWorkItemFactory : IMessageWorkItemFactory
{
    public MessageWorkItem? Create(SocketMessage message)
    {
        if (message is not SocketUserMessage userMessage)
        {
            return null;
        }

        var guildChannel = userMessage.Channel as SocketGuildChannel;
        var permissions = guildChannel?.Guild.CurrentUser.GetPermissions(guildChannel);
        string? forwardedContent = null;
        if (userMessage.ForwardedMessages.Count > 0)
        {
            forwardedContent = string.Join('\n', userMessage.ForwardedMessages.Select(forwarded => forwarded.Message.Content ?? ""));
        }

        return new MessageWorkItem(
            userMessage.Id,
            guildChannel?.Guild.Id ?? 0,
            userMessage.Channel.Id,
            userMessage.Author.Id,
            (userMessage.Author as SocketGuildUser)?.Roles.Select(role => role.Id).ToArray() ?? [],
            userMessage.Content ?? "",
            forwardedContent,
            userMessage.Embeds.Select(embed => new MessageEmbed(embed.Title, embed.Description, embed.Url)).ToArray(),
            permissions?.Has(ChannelPermission.EmbedLinks) ?? false,
            permissions?.Has(ChannelPermission.ManageMessages) ?? false,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
    }
}
