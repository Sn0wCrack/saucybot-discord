using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.Twitter;

public class TwitterImageSyndicationClient : ITwitterImageSyndicationClient
{
    private const string BaseUrl = "https://cdn.syndication.twimg.com";

    private readonly ILogger<TwitterImageSyndicationClient> _logger;
    
    private readonly ICacheManager _cache;
    
    private readonly HttpClient _client = new();

    public TwitterImageSyndicationClient(ILogger<TwitterImageSyndicationClient> logger, ICacheManager cacheManager)
    {
        _logger = logger;
        _cache = cacheManager;
        
        _client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SaucyBot", Assembly.GetEntryAssembly()?.GetName().Version?.ToString())    
        );
        
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }
    
    public async Task<TwitterImageSyndicationTweet?> GetTweet(string identifier)
    {
        var response = await _cache.Remember(
            $"twitter_image_syndication.tweet_{identifier}",
            async () => await _client.GetStringAsync($"{BaseUrl}/tweet-result?id={identifier}")
        );

        return response is null ? null : JsonSerializer.Deserialize<TwitterImageSyndicationTweet?>(response);
    }
}

#region Response Types

public sealed record TwitterImageSyndicationTweet(
    [property: JsonPropertyName("text")]
    string Text,
    [property: JsonPropertyName("user")]
    TwitterImageSyndicationUser User,
    [property: JsonPropertyName("favorites_count")]
    int Likes,
    [property: JsonPropertyName("possibly_sensitive")]
    bool PossiblySensitive,
    [property: JsonPropertyName("created_at")]
    string CreatedAt,
    [property: JsonPropertyName("mediaDetails")]
    List<TwitterImageSyndicationMediaDetail>? MediaDetails,
    [property: JsonPropertyName("photos")]
    List<TwitterImageSyndicationPhoto>? Photos,
    [property: JsonPropertyName("video")]
    List<TwitterImageSyndicationVideo>? Videos,
    [property: JsonPropertyName("conversation_count")]
    int Replies,
    [property: JsonPropertyName("parent")]
    TwitterImageSyndicationTweet Parent
);

public sealed record TwitterImageSyndicationUser(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("screen_name")]
    string ScreenName,
    [property: JsonPropertyName("profile_image_url_https")]
    string ProfileImageUrl 
);

public sealed record TwitterImageSyndicationMediaDetail(
    [property: JsonPropertyName("media_url_https")]
    string MediaUrl,
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("video_info")]
    TwitterImageSyndicationMediaDetailVideoInfo? VideoInfo
);

public sealed record TwitterImageSyndicationMediaDetailVideoInfo(
    [property: JsonPropertyName("variants")]
    List<TwitterImageSyndicationMediaDetailVideoInfo> Variants
);

public sealed record TwitterImageSyndicationMediaDetailVideoInfoVariant(
    [property: JsonPropertyName("bitrate")]
    int Bitrate,
    [property: JsonPropertyName("content_type")]
    string Type,
    [property: JsonPropertyName("url")]
    string Url
);

public sealed record TwitterImageSyndicationPhoto(
    [property: JsonPropertyName("url")]
    string Url
);

public sealed record TwitterImageSyndicationVideo(
    [property: JsonPropertyName("variants")]
    List<TwitterImageSyndicationVideoVariant> Variants
);

public sealed record TwitterImageSyndicationVideoVariant(
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("src")]
    string Url
 );

#endregion
