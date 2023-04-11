using Discord;
using Discord.WebSocket;
using SaucyBot.Extensions;
using SaucyBot.Extensions.Discord;
using SaucyBot.Library;
using SaucyBot.Site.Response;

namespace SaucyBot.Services;

public sealed class MessageManager
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
        var messages = await PartitionMessages(response);
        
        foreach (var message in messages)
        {
            if (message.IsEmpty())
            {
                _logger.LogDebug("Empty message was created from: \"{OriginalMessage}\"", received.Content);
                continue;
            }
            
            await received.ReplyAsync(
                message.Files,
                message.Content,
                allowedMentions: AllowedMentions.None,
                embeds: message.Embeds.ToArray()
            );
        }
    }

    public async Task Send(SocketSlashCommand received, ProcessResponse response)
    {
        var messages = await PartitionMessages(response);
        
        foreach (var message in messages)
        {
            if (message.IsEmpty())
            {
                _logger.LogDebug("Empty message was created from: \"{OriginalMessage}\"", received.Data.ToString());
                continue;
            }

            await received.FollowupWithFilesAsync(
                message.Files,
                message.Content,
                allowedMentions: AllowedMentions.None,
                embeds: message.Embeds.ToArray()
            );
        }
    }

    public async Task<List<Message>> PartitionMessages(ProcessResponse response)
    {
        return response switch
        {
            { Embeds.Count: > 1 } => await HandleMultipleEmbeds(response),
            { Embeds.Count: 1 } => await HandleSingleEmbed(response),
            { Files.Count: >= 1 } => await HandleFiles(response),
            _ => new List<Message> { new(response.Text ?? "") },
        };
    }

    private Task<List<Message>> HandleFiles(ProcessResponse response)
    {
        var messages = new List<Message>();

        if (response.Text is not null)
        {
            messages.Add(new Message(response.Text));
        }

        if (response.Files.Count == 1)
        {
            messages.Add(new Message(Files: response.Files));

            return Task.FromResult(messages);
        }

        // We split up file messages into groups of files under the file size limit
        // This is faster than sending the images back one-by-one
        var segments = new List<List<FileAttachment>>();
        
        foreach (var file in response.Files)
        {
            if (segments.Count == 0)
            {
                segments.Add(new List<FileAttachment> { file });
                continue;
            }

            var index = segments.Count - 1;

            // If we're about to reach maximum message size, move onto the next index
            // If we've reached the end of the array, add a new item to the array as well
            var totalSize = segments[index].Aggregate(0L, (accumulator, item) => accumulator + item.Stream.Length);

            if (file.Stream.Length + totalSize >= Constants.MaximumFileSize)
            {
                segments.Add(new List<FileAttachment> { file });
                continue;
            }
            
            // If we've not reached the maximum message size, add to the current index
            segments[index].Add(file);
        }

        messages.AddRange(segments.Select(files => new Message(Files: files)));

        return Task.FromResult(messages);
    }

    private Task<List<Message>> HandleSingleEmbed(ProcessResponse response)
    {
        var messages = new List<Message>();

        var embed = response.Embeds.First();

        var message = new Message(
            response.Text, 
            new List<Embed> { embed },
            response.Files
        );
        
        messages.Add(message);
        
        return Task.FromResult(messages);
    }

    private Task<List<Message>> HandleMultipleEmbeds(ProcessResponse response)
    {
        var messages = new List<Message>();

        if (response.Text is not null)
        {
            messages.Add(new Message(response.Text));
        }

        for (var i = 0; i < response.Embeds.Count - 1; i += Constants.MaximumEmbedsPerMessage)
        {
            var chunk = response.Embeds.SafeSlice(i, i + Constants.MaximumEmbedsPerMessage);
            var files = new List<FileAttachment>();

            foreach (var embed in chunk)
            {
                var relatedFiles = GetRelatedFiles(embed, response.Files);
                files.AddRange(relatedFiles);
            }
            
            messages.Add(new Message(Embeds: chunk, Files: files));
        }
        
        return Task.FromResult(messages);
    }

    private List<FileAttachment> GetRelatedFiles(IEmbed embed, IEnumerable<FileAttachment> files)
    {
        var embedUrls = new List<string>();

        if (embed.Image?.Url is not null)
        {
            embedUrls.Add(embed.Image?.Url.Replace("attachment://", "")!);
        }

        if (embed.Video?.Url is not null)
        {
            embedUrls.Add(embed.Video?.Url.Replace("attachment://", "")!);
        }

        return files
            .Where(item => embedUrls.Contains(item.FileName))
            .ToList();
    }
}

public record Message(
    string? Content = null,
    List<Embed>? Embeds = null,
    List<FileAttachment>? Files = null
)
{
    public List<Embed> Embeds { get; } = Embeds ?? new List<Embed>();
    public List<FileAttachment> Files { get; } = Files ?? new List<FileAttachment>();

    public bool IsEmpty()
    {
        return Content is null or "" && !Embeds.Any() && !Files.Any();
    }
}
