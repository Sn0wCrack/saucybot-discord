using System.Text;
using Discord;

namespace SaucyBot.Extensions.Discord;

public static class MessageExtensions
{
    public static async Task<IUserMessage> ReplyAsync(
        this IUserMessage msg,
        IEnumerable<FileAttachment>? attachments,
        string? text = null,
        bool isTTS = false,
        Embed? embed = null,
        RequestOptions? options = null,
        AllowedMentions? allowedMentions = null,
        MessageComponent? components = null,
        ISticker[]? stickers = null,
        Embed[]? embeds = null,
        MessageFlags flags = MessageFlags.None)
    {
        return await msg.Channel.SendFilesAsync(attachments, text, isTTS, embed, options, allowedMentions, new MessageReference(new ulong?(msg.Id)), components, stickers, embeds, flags)
            .ConfigureAwait(false);
    }

    public static string AllMessageContent(this IUserMessage msg)
    {
        var builder = new StringBuilder(msg.Content ?? "");

        foreach (var forwarded in msg.ForwardedMessages)
        {
            builder.AppendLine(forwarded.Message.Content ?? "");
        }

        return builder.ToString();
    }

    public static string AllMessageCleanContent(this IUserMessage msg)
    {
        var builder = new StringBuilder(msg.CleanContent ?? "");

        foreach (var forwarded in msg.ForwardedMessages)
        {
            builder.AppendLine(forwarded.Message.CleanContent ?? "");
        }

        return builder.ToString();
    }
}
