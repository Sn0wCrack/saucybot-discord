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
            
            switch (message)
            {
                case ComponentsV2Message c:
                    await received.ReplyAsync(
                        c.Files,
                        c.Content,
                        allowedMentions: AllowedMentions.None,
                        components: c.Components,
                        flags: MessageFlags.ComponentsV2
                    );
                    break;
                case EmbedMessage e:
                    await received.ReplyAsync(
                        e.Files,
                        e.Content,
                        allowedMentions: AllowedMentions.None,
                        embeds: e.Embeds.ToArray()
                    );
                    break;
                default:
                    await received.ReplyAsync(
                        message.Content,
                        allowedMentions: AllowedMentions.None
                    );
                    break;
            }
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

            switch (message)
            {
                case ComponentsV2Message c:
                    await received.FollowupWithFilesAsync(
                        c.Files,
                        c.Content,
                        allowedMentions: AllowedMentions.None,
                        components: c.Components,
                        flags: MessageFlags.ComponentsV2
                    );
                    break;
                case EmbedMessage e:
                    await received.FollowupWithFilesAsync(
                        e.Files,
                        e.Content,
                        allowedMentions: AllowedMentions.None,
                        embeds: e.Embeds.ToArray()
                    );
                    break;
                default:
                    await received.FollowupAsync(
                        message.Content,
                        allowedMentions: AllowedMentions.None
                    );
                    break;
            }
        }
    }

    public static async Task<List<Message>> PartitionMessages(ProcessResponse response)
    {
        return response switch
        {
            { Components: not null } => await HandleComponentsV2(response),
            { Embeds.Count: > 1 } => await HandleMultipleEmbeds(response),
            { Embeds.Count: 1 } => await HandleSingleEmbed(response),
            { Files.Count: >= 1 } => await HandleFiles(response),
            _ => [new EmbedMessage { Content = response.Text }],
        };
    }

    private static Task<List<Message>> HandleComponentsV2(ProcessResponse response)
    {
        var files = response.Files.Count > 0 ? response.Files : [];
        
        return Task.FromResult<List<Message>>([
            new ComponentsV2Message
            {
                Content = response.Text,
                Files = files,
                Components = response.Components!,
            }
        ]);
    }

    private static Task<List<Message>> HandleFiles(ProcessResponse response)
    {
        var messages = new List<Message>();

        if (response.Text is not null)
        {
            messages.Add(new EmbedMessage { Content = response.Text });
        }

        if (response.Files.Count == 1)
        {
            messages.Add(new EmbedMessage { Files = response.Files });

            return Task.FromResult(messages);
        }

        var segments = new List<List<FileAttachment>>();
        
        foreach (var file in response.Files)
        {
            if (segments.Count == 0)
            {
                segments.Add([file]);
                continue;
            }

            var index = segments.Count - 1;

            var totalSize = segments[index].Aggregate(0L, (accumulator, item) => accumulator + item.Stream.Length);

            if (file.Stream.Length + totalSize >= Constants.MaximumFileSize)
            {
                segments.Add([file]);
                continue;
            }
            
            segments[index].Add(file);
        }

        messages.AddRange(segments.Select(files => new EmbedMessage { Files = files }));

        return Task.FromResult(messages);
    }

    private static Task<List<Message>> HandleSingleEmbed(ProcessResponse response)
    {
        var messages = new List<Message>();

        var embed = response.Embeds.First();
        
        var message = new EmbedMessage
        {
            Embeds = [embed],
            Files = response.Files,
        };
        
        messages.Add(message);
        
        if (response.Text is not null)
        {
            messages.Add(new EmbedMessage { Content = response.Text });
        }
        
        return Task.FromResult(messages);
    }

    private static Task<List<Message>> HandleMultipleEmbeds(ProcessResponse response)
    {
        var messages = new List<Message>();

        if (response.Text is not null)
        {
            messages.Add(new EmbedMessage { Content = response.Text });
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
            
            messages.Add(new EmbedMessage { Embeds = chunk, Files = files });
        }
        
        return Task.FromResult(messages);
    }

    private static List<FileAttachment> GetRelatedFiles(Embed embed, IEnumerable<FileAttachment> files)
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

public abstract record Message
{
    public string? Content { get; init; }
    public List<FileAttachment> Files { get; init; } = [];

    public virtual bool IsEmpty() => Content is null or "" && Files.Count == 0;
}

public sealed record EmbedMessage : Message
{
    public List<Embed> Embeds { get; init; } = [];
    
    public override bool IsEmpty() => base.IsEmpty() && Embeds.Count == 0;
}

public sealed record ComponentsV2Message : Message
{
    public required MessageComponent Components { get; init; }
}
