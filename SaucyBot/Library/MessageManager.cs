using Discord;
using Discord.WebSocket;
using SaucyBot.Site.Response;

namespace SaucyBot.Library;

public class MessageManager
{
    private readonly ILogger<MessageManager> _logger;
    private readonly IConfiguration _configuration;

    public MessageManager(ILogger<MessageManager> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task Send(SocketUserMessage received, ProcessResponse response)
    {
        var messages = response switch
        {
            ProcessResponse pr when pr.Embeds.Count > 1 => await HandleMultipleEmbeds(response),
            ProcessResponse pr when pr.Embeds.Count == 1 => await HandleSingleEmbed(response),
            ProcessResponse pr when pr.Files.Count >= 1 => await HandleFiles(response),
            _ => new List<Message> { new(MessageType.Reply, response.Text ?? "") },
        };

        foreach (var message in messages)
        {
            switch (message.Type)
            {
                case MessageType.File:
                    await received.Channel.SendFilesAsync(
                        message.Files,
                        message.Content,
                        allowedMentions: new()
                        {
                            MentionRepliedUser = false
                        },
                        messageReference: new(received.Id)
                    );
                    break;
                case MessageType.Reply:
                    await received.ReplyAsync(
                        message.Content,
                        allowedMentions: new()
                        {
                            MentionRepliedUser = false
                        },
                        embeds: message.Embeds?.ToArray()
                    );
                    break;
            }
        }
    }

    public async Task<List<Message>> HandleFiles(ProcessResponse response)
    {
        var messages = new List<Message>();

        if (response.Text != null)
        {
            messages.Add(new Message(MessageType.Reply, response.Text));
        }

        if (response.Files.Count == 1)
        {
            messages.Add(new Message(MessageType.File, Files: response.Files));

            return messages;
        }

        // We split up file messages into groups of files under the file size limit
        // This is faster than sending the images back one-by-one
        var segments = new List<List<FileAttachment>>();
        
        foreach (var file in response.Files)
        {
            if (segments.Count == 0)
            {
                segments.Add(new List<FileAttachment> { file });
                continue;;
            }

            var index = segments.Count - 1;

            // If we're about to reach maximum message size, move onto the next index
            // If we've reached the end of the array, add a new item to the array as well
            var totalSize = segments[index].Aggregate(0L, (accumulator, item) => accumulator + item.Stream.Length);

            if (file.Stream.Length + totalSize >= Constants.MaximumFilesize)
            {
                segments.Add(new List<FileAttachment> { file });
                continue;
            }
            
            // If we've not reached the maximum message size, add to the current index
            segments[index].Add(file);
        }

        foreach (var files in segments)
        {
            messages.Add(new Message(MessageType.File, Files: files));
        }
        
        return messages;
    }

    public async Task<List<Message>> HandleSingleEmbed(ProcessResponse response)
    {
        var messages = new List<Message>();
        
        messages.Add(new Message(MessageType.Reply, response.Text, response.Embeds));
        
        return messages;
    }

    public async Task<List<Message>> HandleMultipleEmbeds(ProcessResponse response)
    {
        var messages = new List<Message>();
        
        messages.Add(new Message(MessageType.Reply, response.Text, response.Embeds));
        
        return messages;
    }
}

public enum MessageType
{
    Reply,
    File
}

public record Message(
    MessageType Type,
    string? Content = null,
    List<Embed>? Embeds = null,
    List<FileAttachment>? Files = null
);