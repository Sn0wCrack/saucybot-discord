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

public sealed partial class FxTwitter : BaseSite
{
    public override string Identifier => "FxTwitter";

    [GeneratedRegex(@"https?://(www\.|mobile\.)?(?<domain>twitter|x|nitter)\.(com|net)/(?<user>.*)/status/(?<id>\d+)(/(video|photo)/\d{1})?(/(?<translate>\w{2}|\w{5}))?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex FxTwitterPattern();

    protected override Regex Pattern => FxTwitterPattern();

    protected override Color Color => new(0x1DA1F2);

    private readonly ILogger<FxTwitter> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly IFxTwitterClient _client;

    public FxTwitter(ILogger<FxTwitter> logger, IConfiguration configuration, IFxTwitterClient client)
    {
        _logger = logger;
        _configuration = configuration;

        _httpClient = new HttpClient();
        
        _client = client;
    }
    
    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        var response = await _client.GetTweet(
            match.Groups["user"].Value,
            match.Groups["id"].Value,
            match.Groups["translate"].Success ? match.Groups["translate"].Value : null
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
        
        // TODO: Handle quote tweet chains similar to fxtwitter and vxtwitter
        
        if (mainTweetHasMedia)
        {
            return mainTweetHasVideo
                ? await HandleVideo(tweet, videoMedia, mainTweetHasMedia)
                : HandlePhoto(tweet, photoMedia, mainTweetHasMedia);
        }
        
        if (quotedTweetHasMedia)
        {
            return quotedTweetHasVideo
                ? await HandleVideo(tweet, videoMedia, mainTweetHasMedia)
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

    private async Task<ProcessResponse?> HandleVideo(FxTwitterTweet tweet, IEnumerable<VideoResult> results, bool mainTweetHasMedia)
    {
        _logger.LogDebug("Processing as video embed");
        
        var response = new ProcessResponse();

        var video = mainTweetHasMedia
            ? results.FirstOrDefault(result => result.Source == ResultSource.MainTweet)
            : results.FirstOrDefault(result => result.Source == ResultSource.QuotedTweet);

        if (video is null)
        {
            return null;
        }

        var variants = new List<string> { video.Video.Url };

        var variant = await DetermineHighestUsableQualityFile(variants);

        if (variant is not null)
        {
            var videoFile = await GetFile(variant);

            response.Files.Add(videoFile);
        }
        else
        {
            response.Text = $"https://fxtwitter.com/{tweet.Author.ScreenName}/status/{tweet.Id}";
            
            return response;
        }
        
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
                ImageUrl = this.GetOriginalResolutionPhotoUrl(photo.Photo.Url),
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

    private string GetTweetText(FxTwitterTweet tweet)
    {
        return tweet.Translation is not null ? tweet.Translation.Text : tweet.Text;
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

    private string GetOriginalResolutionPhotoUrl(string url)
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
