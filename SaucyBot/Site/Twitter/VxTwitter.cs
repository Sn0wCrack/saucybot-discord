using System.Text.RegularExpressions;
using Discord;
using SaucyBot.Common;
using SaucyBot.Extensions;
using SaucyBot.Library;
using SaucyBot.Library.Sites.Twitter;

namespace SaucyBot.Site.Twitter;

[SiteIdentifier("Twitter")]
public sealed partial class VxTwitterSite : BaseSite
{
    [GeneratedRegex(@"https?://(www\.|mobile\.)?(?<domain>twitter|x|nitter)\.(com|net)/(?<user>\S*)/status/(?<id>\d+)(/(video|photo)/\d{1})?(/(?<translate>\w{2}|\w{5}|original))?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex VxTwitterPattern();

    public override Regex Pattern => VxTwitterPattern();

    public override Color Color => new(0x1DA1F2);

    private readonly ILogger<VxTwitterSite> _logger;
    private readonly IVxTwitterClient _client;

    public VxTwitterSite(ILogger<VxTwitterSite> logger, IVxTwitterClient client)
    {
        _logger = logger;
        _client = client;
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        var tweet = await _client.GetTweet(
            request.Match.Groups["user"].Value,
            request.Match.Groups["id"].Value);

        if (tweet is null)
        {
            return null;
        }

        var description = Helper.EscapeDiscordMarkdown(tweet.Text);
        if (description.Length >= Constants.MaximumEmbedBodyLength)
        {
            return CreateLinkResponse(tweet);
        }

        var media = tweet.MediaExtended;
        if (media.Any(item => item.Type.Equals("video", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogDebug("Processing VxTwitter tweet as video link");
            return CreateLinkResponse(tweet);
        }

        var images = media
            .Where(item => item.Type.Equals("image", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (images.Count == 0)
        {
            return new ProcessResponse
            {
                Embeds = { CreateEmbed(tweet, description) },
            };
        }

        var response = new ProcessResponse();
        foreach (var image in images)
        {
            response.Embeds.Add(CreateEmbed(tweet, description, image.Url));
        }

        return response;
    }

    private ProcessResponse CreateLinkResponse(VxTwitterResponse tweet) => new()
    {
        Text = $"https://vxtwitter.com/{tweet.UserScreenName}/status/{tweet.TweetId}",
    };

    private Embed CreateEmbed(VxTwitterResponse tweet, string description, string? imageUrl = null)
    {
        return new EmbedBuilder
        {
            Url = tweet.TweetUrl,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(tweet.DateEpoch),
            Color = Color,
            Description = description,
            Author = new EmbedAuthorBuilder
            {
                Name = $"{tweet.UserName} (@{tweet.UserScreenName})",
                Url = $"https://twitter.com/{tweet.UserScreenName}",
                IconUrl = tweet.UserProfileImageUrl,
            },
            Fields = new List<EmbedFieldBuilder>
            {
                new()
                {
                    Name = "Replies",
                    Value = tweet.Replies,
                    IsInline = true,
                },
                new()
                {
                    Name = "Retweets",
                    Value = tweet.Retweets,
                    IsInline = true,
                },
                new()
                {
                    Name = "Likes",
                    Value = tweet.Likes,
                    IsInline = true,
                },
            },
            ImageUrl = imageUrl,
            Footer = new EmbedFooterBuilder { IconUrl = Constants.TwitterIconUrl, Text = "Twitter" },
        }.Build();
    }
}
