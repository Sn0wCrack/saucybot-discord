using System.Text.RegularExpressions;
using CoreTweet;
using Discord;
using Discord.WebSocket;
using SaucyBot.Extensions;
using SaucyBot.Library;
using SaucyBot.Services;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public class Twitter : BaseSite
{
    public override string Identifier => "Twitter";

    protected override string Pattern =>
        @"https?:\/\/(www\.|mobile\.)?twitter\.com\/(?<user>.*)\/status\/(?<id>\d+)(\?\=.*)?";

    protected override Color Color => new(0x1DA1F2);

    private readonly ILogger<Twitter> _logger;
    private readonly IConfiguration _configuration;
    private readonly CacheManager _cache;
    private readonly HttpClient _httpClient;
    private readonly Tokens _client;

    public Twitter(ILogger<Twitter> logger, IConfiguration configuration, CacheManager cache)
    {
        _logger = logger;
        _configuration = configuration;
        _cache = cache;

        _httpClient = new HttpClient();
        
        _client = Tokens.Create(
            configuration.GetSection("Sites:Twitter:ConsumerKey").Get<string>(),
            configuration.GetSection("Sites:Twitter:consumerSecret").Get<string>(),
            configuration.GetSection("Sites:Twitter:AccessToken").Get<string>(),
            configuration.GetSection("Sites:Twitter:AccessSecret").Get<string>()
        );

    }
    
    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        var hasTwitterEmbed = false;
        
        // If we have a message attached, we need to wait a bit for Discord to process the embed,
        // we when need to refresh the message and see if an embed has been added in that time.
        if (message is not null)
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.GetSection("Sites:Twitter:Delay").Get<double>()));
            
            // TODO: Refresh message somehow, unsure on how to do that in Discord.NET

            hasTwitterEmbed = message.Embeds.Any(item =>
            {
                var isTwitterEmbed = item.Url.Contains("twitter.com") || item.Url.Contains("t.co");

                return isTwitterEmbed && item?.Author is not null;
            });
        }


        var id = long.Parse(match.Groups["id"].Value);
        
        var tweet = await GetTweet(id);

        if (tweet is null)
        {
            return null;
        }
        
        var videoMedia = await FindVideoElement(tweet);

        // Only try and embed this twitter link if one of the following is true:
        //  - Discord has failed to create an embed for Twitter
        //  - The result is "sensitive" and it has a video, as Discord often fails to play these inline

        if (videoMedia is not null && (tweet.PossiblySensitive ?? false))
        {
            return await HandleVideo(tweet, match.Value, !hasTwitterEmbed);
        }

        if (hasTwitterEmbed)
        {
            return null;
        }

        return await HandleRegular(tweet, match.Value);
    }

    private Task<MediaEntity?> FindVideoElement(StatusResponse status)
    {
        var video = status.ExtendedEntities?.Media.FirstOrDefault(item => item.Type.IsIn("video", "animated_gif"));

        if (video is null && (status.QuotedStatusId.HasValue))
        {
            var quotedVideo = status.QuotedStatus?.ExtendedEntities?.Media.FirstOrDefault(item => item.Type.IsIn("video", "animated_gif"));

            return Task.FromResult(quotedVideo);
        }

        return Task.FromResult(video);

    }

    private Task<IEnumerable<MediaEntity>?> FindAllPhotoElements(StatusResponse status)
    {
        var photos = status.ExtendedEntities?.Media.Where(item => item.Type == "photo");

        if (photos is null && (status.QuotedStatusId.HasValue))
        {
            var quotedPhotos = status.QuotedStatus?.ExtendedEntities?.Media.Where(item => item.Type == "photo");

            return Task.FromResult(quotedPhotos);
        }

        return Task.FromResult(photos);
    }

    private async Task<ProcessResponse?> HandleVideo(StatusResponse status, string url, bool makeEmbed = false)
    {
        var response = new ProcessResponse();
        
        var video = await FindVideoElement(status);

        if (video is null)
        {
            return null;
        }

        var variants = video.VideoInfo.Variants
            .Where(item => item.Bitrate.HasValue)
            .OrderBy(item => item?.Bitrate ?? 0)
            .Select(item => item.Url);

        var variant = await DetermineHighestUsableQualityFile(variants);

        if (variant is null)
        {
            return null;
        }

        var videoFile = await GetFile(variant);
        
        response.Files.Add(videoFile);

        if (!makeEmbed)
        {
            return response;
        }
        
        // TODO: When Discord add video embeds, come back here and add that
        var embed = new EmbedBuilder
        {
            Url = url,
            Timestamp = status.CreatedAt,
            Color = this.Color,
            Description = status.FullText,
            Author = new EmbedAuthorBuilder
            {
                Name = $"{status.User.Name} (@{status.User.ScreenName}",
                IconUrl = status.User.ProfileImageUrlHttps,
                Url = $"https://twitter.com/{status.User.ScreenName}",
            },
            Fields = new List<EmbedFieldBuilder>
            {
                new ()
                {
                    Name = "Likes",
                    Value = status.FavoriteCount ?? 0,
                    IsInline = true
                },
                new ()
                {
                    Name = "Retweets",
                    Value = status.RetweetCount ?? 0,
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
    
    public async Task<HttpResponseMessage> PokeFile(string url)
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
    
    private async Task<ProcessResponse?> HandleRegular(StatusResponse status, string url)
    {
        var response = new ProcessResponse();

        var photos = await FindAllPhotoElements(status);

        if (photos is null)
        {
            return null;
        }

        foreach (var photo in photos)
        {
            var embed = new EmbedBuilder
            {
                Url = url,
                Timestamp = status.CreatedAt,
                Color = this.Color,
                Description = status.FullText,
                Author = new EmbedAuthorBuilder
                {
                    Name = $"{status.User.Name} (@{status.User.ScreenName}",
                    IconUrl = status.User.ProfileImageUrlHttps,
                    Url = $"https://twitter.com/{status.User.ScreenName}",
                },
                Fields = new List<EmbedFieldBuilder>
                {
                    new ()
                    {
                        Name = "Likes",
                        Value = status.FavoriteCount,
                        IsInline = true
                    },
                    new ()
                    {
                        Name = "Retweets",
                        Value = status.RetweetCount,
                        IsInline = true
                    },
                },
                ImageUrl = photo.MediaUrlHttps,
                Footer = new EmbedFooterBuilder { IconUrl = Constants.TwitterIconUrl, Text = "Twitter" },
            };
            
            response.Embeds.Add(embed.Build());
        }
        
        return response;
    }

    private async Task<StatusResponse?> GetTweet(long id)
    {
        return await _cache.Remember<StatusResponse?>($"twitter.tweet_{id}", async () => await _client.Statuses.ShowAsync(
            id: id,
            include_entities: true,
            trim_user: false,
            tweet_mode: TweetMode.Extended
        ));
    }
}
