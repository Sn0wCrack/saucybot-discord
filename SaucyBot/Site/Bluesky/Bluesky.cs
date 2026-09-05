using System.Text.RegularExpressions;
using Discord;
using SaucyBot.Extensions;
using SaucyBot.Library;
using SaucyBot.Library.Sites.BlueSky;

namespace SaucyBot.Site.Bluesky;


public sealed partial class BlueskySite : BaseSite, IBlueskySite
{
    public override string Identifier => "Bluesky";

    [GeneratedRegex(@"https?://(www\.)?bsky\.app/profile/(?<user>\S*)/post/(?<id>\S*)/?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex BlueskyPattern();

    public override Regex Pattern => BlueskyPattern();

    public override Color Color => new(0x1083FE);

    private readonly ILogger<BlueskySite> _logger;
    private readonly IConfiguration _configuration;
    private readonly IVixBlueskyClient _client;
    private readonly TimeProvider _timeProvider;

    public BlueskySite(
        ILogger<BlueskySite> logger,
        IConfiguration configuration,
        IVixBlueskyClient client,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _client = client;
        _timeProvider = timeProvider;
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        var response = await _client.GetPost(
            request.Match.Groups["user"].Value,
            request.Match.Groups["id"].Value
        );

        var url = $"https://bsky.app/profile/{request.Match.Groups["user"].Value}/post/{request.Match.Groups["id"].Value}";

        var post = response?.Posts.FirstOrDefault();

        if (post is null)
        {
            return null;
        }

        var hasEmbed = false;

        // If we have a message attached, we need to wait a bit for Discord to process the embed,
        // we when need to refresh the message and see if an embed has been added in that time.
        if (request.Context?.Message is { } message)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_configuration.GetSection("Sites:Bluesky:Delay").Get<double>()),
                _timeProvider,
                request.Context.CancellationToken);

            // NOTE: Discord.NET works a little interestingly, basically when a message updates the Bot learns of this change
            // and then proceeds to update its internal cache, so while we're waiting around it should update the message cache
            // automatically, so there's no need to refresh the message object.

            hasEmbed = (await message.GetLatestEmbedsAsync(request.Context.CancellationToken)).Count != 0;
        }

        if (hasEmbed)
        {
            return null;
        }

        var photoMedia = await FindAllPhotoElements(post);

        var videoMedia = await FindAllVideoElements(post);

        var hasPhoto = photoMedia.NotEmpty();

        var hasVideo = videoMedia.NotEmpty();

        if (hasVideo)
        {
            return HandleVideoLazy(post, request.Match);
        }

        if (hasPhoto)
        {
            return HandlePhoto(url, post, photoMedia);
        }

        return HandleRegular(url, post);
    }

    private Task<List<VixBlueskyEmbedImage>> FindAllPhotoElements(VixBlueskyPost post)
    {
        return Task.FromResult(post.Embed?.Images ?? post.Embed?.Media?.Images ?? []);
    }

    private Task<List<string>> FindAllVideoElements(VixBlueskyPost post)
    {
        var output = new List<string>();

        if (post.Embed?.Playlist is not null)
        {
            output.Add(post.Embed.Playlist);
        }

        return Task.FromResult(output);
    }


    private ProcessResponse HandlePhoto(string url, VixBlueskyPost post, IEnumerable<VixBlueskyEmbedImage> results)
    {
        _logger.LogDebug("Processing as photo embed");

        var response = new ProcessResponse
        {
            IsNsfw = post.Record.IsNsfw,
        };

        foreach (var image in results)
        {
            var embed = new EmbedBuilder
            {
                Url = url,
                Timestamp = DateTimeOffset.Parse(post.Record.CreatedAt),
                Color = this.Color,
                Description = post.Record.Text,
                Author = new EmbedAuthorBuilder
                {
                    Name = $"{post.Author.DisplayName} (@{post.Author.Handle})",
                    IconUrl = post.Author.AvatarUrl,
                    Url = $"https://bsky.app/profile/{post.Author.Handle}",
                },
                Fields = new List<EmbedFieldBuilder>
                {
                    new ()
                    {
                        Name = "Replies",
                        Value = post.Replies,
                        IsInline = true
                    },
                    new () {
                        Name = "Reposts",
                        Value = post.Reposts,
                        IsInline = true
                    },
                    new ()
                    {
                        Name = "Quotes",
                        Value = post.Quotes,
                        IsInline = true
                    },
                    new ()
                    {
                        Name = "Likes",
                        Value = post.Likes,
                        IsInline = true
                    },
                },
                ImageUrl = image.Url,
                Footer = new EmbedFooterBuilder { IconUrl = Constants.BlueskyIconUrl, Text = "Bluesky" },
            };

            response.Embeds.Add(embed.Build());
        }

        return response;
    }


    private ProcessResponse HandleVideo(string url, Match match, VixBlueskyPost post)
    {
        _logger.LogDebug("Processing as video embed");

        var videoUrl = $"https://r.bskyx.app/profile/{match.Groups["user"].Value}/post/{match.Groups["id"].Value}";

        var response = new ProcessResponse
        {
            IsNsfw = post.Record.IsNsfw,
        };

        var embed = new EmbedBuilder
        {
            Url = url,
            Timestamp = DateTimeOffset.Parse(post.Record.CreatedAt),
            Color = this.Color,
            Description = post.Record.Text,
            Author = new EmbedAuthorBuilder
            {
                Name = $"{post.Author.DisplayName} (@{post.Author.Handle})",
                IconUrl = post.Author.AvatarUrl,
                Url = $"https://bsky.app/profile/{post.Author.Handle}",
            },
            Fields = new List<EmbedFieldBuilder>
            {
                new ()
                {
                    Name = "Replies",
                    Value = post.Replies,
                    IsInline = true
                },
                new () {
                    Name = "Reposts",
                    Value = post.Reposts,
                    IsInline = true
                },
                new ()
                {
                    Name = "Quotes",
                    Value = post.Quotes,
                    IsInline = true
                },
                new ()
                {
                    Name = "Likes",
                    Value = post.Likes,
                    IsInline = true
                },
            },
            Footer = new EmbedFooterBuilder { IconUrl = Constants.BlueskyIconUrl, Text = "Bluesky" },
        };

        response.Embeds.Add(embed.Build());

        response.Text = videoUrl;

        return response;
    }

    private ProcessResponse HandleVideoLazy(VixBlueskyPost post, Match match)
    {
        var response = new ProcessResponse
        {
            Text = $"https://bskyx.app/profile/{match.Groups["user"].Value}/post/{match.Groups["id"].Value}",
            IsNsfw = post.Record.IsNsfw,
        };

        return response;
    }


    private ProcessResponse HandleRegular(string url, VixBlueskyPost post)
    {
        var response = new ProcessResponse
        {
            IsNsfw = post.Record.IsNsfw,
        };

        var embed = new EmbedBuilder
        {
            Url = url,
            Timestamp = DateTimeOffset.Parse(post.Record.CreatedAt),
            Color = this.Color,
            Description = post.Record.Text,
            Author = new EmbedAuthorBuilder
            {
                Name = $"{post.Author.DisplayName} (@{post.Author.Handle})",
                IconUrl = post.Author.AvatarUrl,
                Url = $"https://bsky.app/profile/{post.Author.Handle}",
            },
            Fields = new List<EmbedFieldBuilder>
            {
                new ()
                {
                    Name = "Replies",
                    Value = post.Replies,
                    IsInline = true
                },
                new () {
                    Name = "Reposts",
                    Value = post.Reposts,
                    IsInline = true
                },
                new ()
                {
                    Name = "Quotes",
                    Value = post.Quotes,
                    IsInline = true
                },
                new ()
                {
                    Name = "Likes",
                    Value = post.Likes,
                    IsInline = true
                },
            },
            Footer = new EmbedFooterBuilder { IconUrl = Constants.BlueskyIconUrl, Text = "Bluesky" },
        };

        response.Embeds.Add(embed.Build());

        return response;
    }
}
