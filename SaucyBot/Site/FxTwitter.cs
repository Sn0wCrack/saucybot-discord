using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Extensions;
using SaucyBot.Library;
using SaucyBot.Library.Sites.Twitter;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed class FxTwitter : BaseSite
{
    public override string Identifier => "FxTwitter";

    protected override string Pattern =>
        @"https?:\/\/(www\.|mobile\.)?(?<domain>twitter|x|nitter)\.(com|net)\/(?<user>.*)\/status\/(?<id>\d+)\/?";

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
            match.Groups["id"].Value
        );
        
        if (response is null)
        {
            return null;
        }

        var tweet = response.Tweet;
        
        var hasTwitterEmbed = false;
        
        // If we have a message attached, we need to wait a bit for Discord to process the embed,
        // we when need to refresh the message and see if an embed has been added in that time.
        if (message is not null)
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.GetSection("Sites:Twitter:Delay").Get<double>()));

            // NOTE: Discord.NET works a little interestingly, basically when a message updates the Bot learns of this change
            // and then proceeds to update its internal cache, so while we're waiting around it should update the message cache
            // automatically, so there's no need to refresh the message object.

            hasTwitterEmbed = message.Embeds.Any(item =>
            {
                var isTwitterEmbed = item.Url.Contains("twitter.com") || item.Url.Contains("t.co") || item.Url.Contains("x.com");

                return isTwitterEmbed && item.Author is not null;
            });
        }

        var videoMedia = await FindVideoElement(tweet);

        var shouldEmbedVideo = videoMedia is not null && tweet.PossiblySensitive;
        
        // Only try and embed this twitter link if one of the following is true:
        //  - Discord has failed to create an embed for Twitter
        //  - The result is "sensitive" and it has a video, as Discord often fails to play these inline

        if (hasTwitterEmbed && !shouldEmbedVideo)
        {
            return null;
        }
        
        // TODO: Handle quote tweet chains similar to fxtwitter and vxtwitter
        
        return videoMedia is not null 
            ? await HandleVideo(tweet, match.Value)
            : await HandleRegular(tweet, match.Value);
    }

    private Task<FxTwitterVideo?> FindVideoElement(FxTwitterTweet tweet)
    {
        var video = tweet.Media?.Videos?.FirstOrDefault(item => item.Type.IsIn("video", "gif"));

        if (video is null && tweet.QuotedTweet is not null)
        {
            var quotedVideo = tweet.QuotedTweet?.Media?.Videos?.FirstOrDefault(item => item.Type.IsIn("video", "gif"));

            return Task.FromResult(quotedVideo);
        }

        return Task.FromResult(video);

    }

    private Task<IEnumerable<FxTwitterPhoto>?> FindAllPhotoElements(FxTwitterTweet tweet)
    {
        var photos = tweet.Media?.Photos?.Where(item => item.Type == "photo");

        if (photos is null && tweet.QuotedTweet is not null)
        {
            var quotedPhotos = tweet.QuotedTweet?.Media?.Photos?.Where(item => item.Type == "photo");

            return Task.FromResult(quotedPhotos);
        }

        return Task.FromResult(photos);
    }

    private async Task<ProcessResponse?> HandleVideo(FxTwitterTweet tweet, string url)
    {
        var response = new ProcessResponse();

        var video = await FindVideoElement(tweet);

        if (video is null)
        {
            return null;
        }

        var variants = new List<string> { video.Url };

        var variant = await DetermineHighestUsableQualityFile(variants);
        
        if (variant is null)
        {
            return null;
        }
        
        var videoFile = await GetFile(variant);
        
        response.Files.Add(videoFile);

        // TODO: When Discord add video embeds, come back here and add that
        var embed = new EmbedBuilder
        {
            Url = tweet.Url ?? url,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(tweet.CreatedTimestamp),
            Color = this.Color,
            Description = tweet.Text,
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
        var response = await _httpClient.GetStreamAsync(url);

        var stream = new MemoryStream();
        
        await response.CopyToAsync(stream);
        
        var parsed = new Uri(url);
        
        return new FileAttachment(
            stream,
            Path.GetFileName(parsed.AbsolutePath)
        );
    }
    
    private async Task<ProcessResponse?> HandleRegular(FxTwitterTweet tweet, string url)
    {
        var response = new ProcessResponse();

        var photos = await FindAllPhotoElements(tweet);

        // TODO: Refactor how we build embeds a bit better so I don't have massive amounts of duplicate code
        if (photos is null)
        {
            var embed = new EmbedBuilder
            {
                Url = tweet.Url ?? url,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(tweet.CreatedTimestamp),
                Color = this.Color,
                Description = tweet.Text,
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

        foreach (var photo in photos)
        {
            var embed = new EmbedBuilder
            {
                Url = tweet.Url ?? url,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(tweet.CreatedTimestamp),
                Color = this.Color,
                Description = tweet.Text,
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
                ImageUrl = photo.Url,
                Footer = new EmbedFooterBuilder { IconUrl = Constants.TwitterIconUrl, Text = "Twitter" },
            };
            
            response.Embeds.Add(embed.Build());
        }
        
        return response;
    }
}
