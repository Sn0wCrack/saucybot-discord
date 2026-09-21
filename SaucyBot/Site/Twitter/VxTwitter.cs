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

    [GeneratedRegex(@"(?<!https?://[\w.\-_%$@&?!:;/'()*]+)@([\w.]+)(?=\W|$)", RegexOptions.IgnoreCase)]
    private static partial Regex MentionPattern();

    [GeneratedRegex(@"(?<!https?://[\w.\-_%$@&?!:;/'()*]+)#([\w.]+)(?=\W|$)", RegexOptions.IgnoreCase)]
    private static partial Regex HashtagPattern();

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

        var description = GetTweetText(tweet);
        if (description.Length >= Constants.MaximumEmbedBodyLength)
        {
            return CreateLinkResponse(tweet);
        }

        var mainImages = GetImages(tweet);
        var mainHasVideo = HasVideo(tweet);
        var quotedImages = tweet.QuotedTweet is null ? [] : GetImages(tweet.QuotedTweet);
        var quotedHasVideo = tweet.QuotedTweet is not null && HasVideo(tweet.QuotedTweet);

        if (mainHasVideo || quotedHasVideo)
        {
            _logger.LogDebug("Processing VxTwitter tweet as video link");
            return CreateLinkResponse(tweet);
        }

        var images = mainImages.Any() ? mainImages : quotedImages;

        if (images.Count == 0)
        {
            return new ProcessResponse
            {
                Embeds = { CreateEmbed(tweet, description) },
                IsNsfw = tweet.PossiblySensitive,
            };
        }

        var response = new ProcessResponse
        {
            IsNsfw = tweet.PossiblySensitive
        };

        foreach (var image in images)
        {
            response.Embeds.Add(CreateEmbed(tweet, description, image.Url));
        }

        return response;
    }

    private ProcessResponse CreateLinkResponse(VxTwitterResponse tweet) => new()
    {
        Text = $"https://vxtwitter.com/{tweet.UserScreenName}/status/{tweet.TweetId}",
        IsNsfw = tweet.PossiblySensitive
    };

    private static List<VxTwitterMedia> GetImages(VxTwitterResponse tweet) => tweet.MediaExtended
        .Where(item => item.Type.Equals("image", StringComparison.OrdinalIgnoreCase))
        .ToList();

    private static bool HasVideo(VxTwitterResponse tweet) => tweet.MediaExtended
        .Any(item => item.Type.IsIn(["video", "gif"]));

    private static string GetTweetText(VxTwitterResponse tweet)
    {
        var text = LinkifyTwitterContent(tweet.Text);
        text = Helper.EscapeDiscordMarkdown(text);
        if (tweet.QuotedTweet is null)
        {
            return text;
        }

        var quote = tweet.QuotedTweet;
        var quoteAuthorUrl = $"https://twitter.com/{quote.UserScreenName}";
        return text +
            $"\n\n> **[Quoting]({tweet.TweetUrl}) {quote.UserName} ([@{quote.UserScreenName}]({quoteAuthorUrl}))**\n" +
            GetQuoteText(quote);
    }

    private static string GetQuoteText(VxTwitterResponse quote)
    {
        var quotedText = GetTweetText(quote);
        return quotedText.Insert(0, "> ").Replace("\n", "\n> ");
    }

    private static string LinkifyTwitterContent(string text)
    {
        text = MentionPattern().Replace(text, match =>
        {
            var username = match.Groups[1].Value;
            return $"[@{username}](https://twitter.com/{username})";
        });

        text = HashtagPattern().Replace(text, match =>
        {
            var hashtag = match.Groups[1].Value;
            return $"[#{hashtag}](https://twitter.com/hashtag/{hashtag})";
        });

        return text;
    }

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
