using System.Text.RegularExpressions;
using System.Web;
using Discord;
using Discord.WebSocket;
using SaucyBot.Common;
using SaucyBot.Extensions;
using SaucyBot.Library;
using SaucyBot.Library.Sites.Twitter;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed partial class FxTwitter : BaseSite, ITwitterSite
{
    public override string Identifier => "FxTwitter";

    [GeneratedRegex(@"https?://(www\.|mobile\.)?(?<domain>twitter|x|nitter)\.(com|net)/(?<user>\S*)/status/(?<id>\d+)(/(video|photo)/\d{1})?(/(?<translate>\w{2}|\w{5}|original))?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex FxTwitterPattern();

    [GeneratedRegex(@"(?<!https?://[\w.\-_%$@&?!:;/'()*]+)@([\w.]+)(?=\W|$)", RegexOptions.IgnoreCase)]
    private static partial Regex MentionPattern();

    [GeneratedRegex(@"(?<!https?://[\w.\-_%$@&?!:;/'()*]+)#([\w.]+)(?=\W|$)", RegexOptions.IgnoreCase)]
    private static partial Regex HashtagPattern();

    protected override Regex Pattern => FxTwitterPattern();

    public override Color Color => new(0x1DA1F2);

    private readonly ILogger<FxTwitter> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly IFxTwitterClient _client;

    public FxTwitter(ILogger<FxTwitter> logger, IConfiguration configuration, IFxTwitterClient client, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;

        _httpClient = httpClientFactory.CreateClient("FileDownload");

        _client = client;
    }

    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "ja", "ko", "zh", "de", "fr", "es", "pt", "ru", "it",
        "th", "vi", "id", "ms", "tl", "ar", "hi", "bn", "pl", "tr",
        "nl", "sv", "da", "fi", "el", "cs", "ro", "hu", "uk", "he",
        "nb", "ca"
    };

    private static string? DiscordLocaleToLanguageCode(string? locale)
    {
        if (locale is null || locale.Length < 2)
        {
            return null;
        }

        var code = locale[..2].ToLowerInvariant();

        return SupportedLanguages.Contains(code) ? code : null;
    }

    private string? ResolveTranslationLanguage(ProcessRequest request)
    {
        // 1. Requested language from URL (highest priority)
        if (request.Match.Groups["translate"].Success)
        {
            return request.Match.Groups["translate"].Value;
        }

        var autoDetect = _configuration.GetSection("Sites:FxTwitter:AutoDetectLanguage").Get<bool?>() ?? false;

        if (!autoDetect)
        {
            return null;
        }

        // 2. Guild locale (only reliable for community servers)
        var guild = request.Guild;

        if (guild is not null && guild.Features.HasFeature(GuildFeature.Community))
        {
            var guildCode = DiscordLocaleToLanguageCode(guild.PreferredLocale);
            if (guildCode is not null)
            {
                return guildCode;
            }
        }

        // 3. User locale (only available on slash commands)
        var userCode = DiscordLocaleToLanguageCode(request.UserLocale);
        if (userCode is not null)
        {
            return userCode;
        }

        // 4. Original (no translation)
        return null;
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        var translation = ResolveTranslationLanguage(request);

        _logger.LogDebug("Using {Translation} as language for Tweet translation", translation ?? "Original");

        var response = await _client.GetTweet(
            request.Match.Groups["user"].Value,
            request.Match.Groups["id"].Value,
            translation
        );

        if (response is null)
        {
            return null;
        }

        var tweet = response.Tweet;

        var photoMedia = await FindAllPhotoElements(tweet);

        var videoMedia = await FindAllVideoElements(tweet);

        var mainTweetHasPhoto = photoMedia
            .Where(result => result.Source == ResultSource.MainTweet)
            .NotEmpty();

        var mainTweetHasVideo = videoMedia
            .Where(result => result.Source == ResultSource.MainTweet)
            .NotEmpty();

        var quotedTweetHasPhoto = photoMedia
            .Where(result => result.Source == ResultSource.QuotedTweet)
            .NotEmpty();

        var quotedTweetHasVideo = videoMedia
            .Where(result => result.Source == ResultSource.QuotedTweet)
            .NotEmpty();

        var mainTweetHasMedia = mainTweetHasPhoto || mainTweetHasVideo;

        var quotedTweetHasMedia = quotedTweetHasPhoto || quotedTweetHasVideo;

        if (mainTweetHasMedia)
        {
            return mainTweetHasVideo
                ? HandleVideo(tweet)
                : HandlePhoto(tweet, photoMedia, mainTweetHasMedia);
        }

        if (quotedTweetHasMedia)
        {
            return quotedTweetHasVideo
                ? HandleVideo(tweet)
                : HandlePhoto(tweet, photoMedia, mainTweetHasMedia);
        }

        return HandleRegular(tweet);
    }

    private Task<List<VideoResult>> FindAllVideoElements(FxTwitterTweet tweet)
    {
        var output = new List<VideoResult>();

        var videos = tweet
            .Media?
            .Videos?
            .Where(item => item.Type.IsIn("video", "gif"));

        if (videos is not null)
        {
            output.AddRange(videos.Select(video => new VideoResult(video, ResultSource.MainTweet)));
        }

        var quotedVideos = tweet
            .QuotedTweet?
            .Media?
            .Videos?
            .Where(item => item.Type.IsIn("video", "gif"));

        if (quotedVideos is not null)
        {
            output.AddRange(quotedVideos.Select(video => new VideoResult(video, ResultSource.QuotedTweet)));
        }

        return Task.FromResult(output);
    }

    private Task<List<PhotoResult>> FindAllPhotoElements(FxTwitterTweet tweet)
    {
        var output = new List<PhotoResult>();

        var photos = tweet
            .Media?
            .Photos?
            .Where(item => item.Type == "photo");

        if (photos is not null)
        {
            output.AddRange(photos.Select(photo => new PhotoResult(photo, ResultSource.MainTweet)));
        }

        var quotedPhotos = tweet
            .QuotedTweet?
            .Media?
            .Photos?
            .Where(item => item.Type == "photo");

        if (quotedPhotos is not null)
        {
            output.AddRange(quotedPhotos.Select(photo => new PhotoResult(photo, ResultSource.QuotedTweet)));
        }

        return Task.FromResult(output);
    }

    private ProcessResponse HandleVideo(FxTwitterTweet tweet)
    {
        _logger.LogDebug("Processing as video embed");

        var response = new ProcessResponse
        {
            Text = $"https://fxtwitter.com/{tweet.Author.ScreenName}/status/{tweet.Id}",
        };

        return response;
    }


    private ProcessResponse HandlePhoto(FxTwitterTweet tweet, IEnumerable<PhotoResult> results, bool mainTweetHasMedia)
    {
        _logger.LogDebug("Processing as photo embed");

        var response = new ProcessResponse();

        var photos = mainTweetHasMedia
            ? results.Where(result => result.Source == ResultSource.MainTweet).ToList()
            : results.Where(result => result.Source == ResultSource.QuotedTweet).ToList();

        foreach (var photo in photos)
        {
            var embed = new EmbedBuilder
            {
                Url = tweet.Url,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(tweet.CreatedTimestamp),
                Color = this.Color,
                Description = GetTweetText(tweet),
                Author = new EmbedAuthorBuilder
                {
                    Name = $"{tweet.Author.Name} (@{tweet.Author.ScreenName})",
                    IconUrl = tweet.Author.AvatarUrl,
                    Url = $"https://twitter.com/{tweet.Author.ScreenName}",
                },
                Fields = new List<EmbedFieldBuilder>
                {
                    new ()
                    {
                        Name = "Replies",
                        Value = tweet.Replies ?? 0,
                        IsInline = true
                    },
                    new () {
                        Name = "Retweets",
                        Value = tweet.Retweets ?? 0,
                        IsInline = true
                    },
                    new ()
                    {
                        Name = "Likes",
                        Value = tweet.Likes ?? 0,
                        IsInline = true
                    },
                    new ()
                    {
                        Name = "Views",
                        Value = tweet.Views ?? 0,
                        IsInline = true
                    },
                },
                ImageUrl = GetOriginalResolutionPhotoUrl(photo.Photo.Url),
                Footer = new EmbedFooterBuilder { IconUrl = Constants.TwitterIconUrl, Text = "Twitter" },
            };

            response.Embeds.Add(embed.Build());
        }

        return response;
    }

    private ProcessResponse HandleRegular(FxTwitterTweet tweet)
    {
        var response = new ProcessResponse();

        var embed = new EmbedBuilder
        {
            Url = tweet.Url,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(tweet.CreatedTimestamp),
            Color = this.Color,
            Description = GetTweetText(tweet),
            Author = new EmbedAuthorBuilder
            {
                Name = $"{tweet.Author.Name} (@{tweet.Author.ScreenName})",
                IconUrl = tweet.Author.AvatarUrl,
                Url = tweet.Author.Url ?? $"https://twitter.com/{tweet.Author.ScreenName}",
            },
            Fields = new List<EmbedFieldBuilder>
            {
                new ()
                {
                    Name = "Replies",
                    Value = tweet.Replies ?? 0,
                    IsInline = true
                },
                new () {
                    Name = "Retweets",
                    Value = tweet.Retweets ?? 0,
                    IsInline = true
                },
                new ()
                {
                    Name = "Likes",
                    Value = tweet.Likes ?? 0,
                    IsInline = true
                },
                new ()
                {
                    Name = "Views",
                    Value = tweet.Views ?? 0,
                    IsInline = true
                },
            },
            Footer = new EmbedFooterBuilder { IconUrl = Constants.TwitterIconUrl, Text = "Twitter" },
        };

        response.Embeds.Add(embed.Build());

        return response;
    }

    private static string GetTweetText(FxTwitterTweet tweet)
    {
        var text = tweet.Translation is not null ? tweet.Translation.Text : tweet.Text;

        text = LinkifyTwitterContent(text);
        text = Helper.EscapeDiscordMarkdown(text);

        if (tweet.QuotedTweet is null)
        {
            return text;
        }

        var author = tweet.QuotedTweet.Author;

        text += $"\n\n> **[Quoting]({tweet.Url}) {author.Name} ([@{author.ScreenName}]({author.Url}))**\n" +
                GetQuoteText(tweet.QuotedTweet);

        return text;
    }

    private static string GetQuoteText(FxTwitterTweet quote)
    {
        var quotedText = quote.Translation is not null ? quote.Translation.Text : quote.Text;

        quotedText = LinkifyTwitterContent(quotedText);
        quotedText = Helper.EscapeDiscordMarkdown(quotedText);

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

    private async Task<string?> DetermineHighestUsableQualityFile(IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            var response = await PokeFile(url);

            if (response.Content.Headers.ContentLength < Constants.MaximumFileSize)
            {
                return url;
            }
        }

        return null;
    }

    private async Task<HttpResponseMessage> PokeFile(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);

        return await _httpClient.SendAsync(request);
    }

    private async Task<FileAttachment> GetFile(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        var contentLength = response.Content.Headers.ContentLength ?? -1;
        var stream = await response.Content.ReadAsStreamAsync();

        var parsed = new Uri(url);

        return new FileAttachment(
            new KnownLengthStream(stream, contentLength),
            Path.GetFileName(parsed.AbsolutePath)
        );
    }

    private static string GetOriginalResolutionPhotoUrl(string url)
    {
        var uri = new Uri(url);
        var query = uri.Query;
        var queryDictionary = HttpUtility.ParseQueryString(query);
        if (queryDictionary.AllKeys.Contains("name"))
        {
            queryDictionary.Remove("name");
        }
        queryDictionary.Add("name", "orig");
        var builder = new UriBuilder(uri)
        {
            Query = queryDictionary.ToString()
        };
        return builder.Uri.ToString();
    }
}

public enum ResultSource
{
    MainTweet,
    QuotedTweet
};

public sealed record VideoResult(
    FxTwitterVideo Video,
    ResultSource Source
 );

public sealed record PhotoResult(
    FxTwitterPhoto Photo,
    ResultSource Source
);
