using SaucyBot.Common;
using System.Text;
using Discord;

namespace SaucyBot.Extensions.Discord;


public static class MessageExtensions
{
    extension(IUserMessage msg)
    {
        public async Task<IUserMessage> ReplyAsync(
            IEnumerable<FileAttachment>? attachments,
            string? text = null,
            bool isTTS = false,
            Embed? embed = null,
            RequestOptions? options = null,
            AllowedMentions? allowedMentions = null,
            MessageComponent? components = null,
            ISticker[]? stickers = null,
            Embed[]? embeds = null,
            MessageFlags flags = MessageFlags.None
        ) {
            return await msg.Channel.SendFilesAsync(attachments, text, isTTS, embed, options, allowedMentions, new MessageReference(new ulong?(msg.Id)), components, stickers, embeds, flags)
                .ConfigureAwait(false);
        }

        public string AllMessageContent()
        {
            var builder = new StringBuilder(msg.Content ?? "");

            foreach (var forwarded in msg.ForwardedMessages)
            {
                builder.AppendLine(forwarded.Message.Content ?? "");
            }

            return builder.ToString();
        }

        public string AllMessageCleanContent()
        {
            var builder = new StringBuilder(
                Helper.MarkdownToPlainText(msg.Content ?? "")
            );

            foreach (var forwarded in msg.ForwardedMessages)
            {
                builder.AppendLine(
                    Helper.MarkdownToPlainText(forwarded.Message.Content ?? "")
                );
            }

            return builder.ToString().Trim();
        }
    }
}
