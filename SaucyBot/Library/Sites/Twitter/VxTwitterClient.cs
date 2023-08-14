using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.Twitter;

public class VxTwitterClient: IVxTwitterClient
{
    private const string BaseUrl = "https://api.vxtwitter.com";

    private readonly ILogger<VxTwitterClient> _logger;
    
    private readonly ICacheManager _cache;
    
    private readonly HttpClient _client = new();

    public VxTwitterClient(ILogger<VxTwitterClient> logger, ICacheManager cacheManager)
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
    
    public async Task<VxTwitterResponse?> GetTweet(string name, string identifier)
    {
        var response = await _cache.Remember(
            $"vxtwitter.tweet_{name}_{identifier}",
            async () => await _client.GetStringAsync($"{BaseUrl}/{name}/status/{identifier}")
        );

        return response is null ? null : JsonSerializer.Deserialize<VxTwitterResponse>(response);
    }
}


#region Response Types

public sealed record VxTwitterResponse(
    [property: JsonPropertyName("tweetID")]
    string Id,
    [property: JsonPropertyName("date_epoch")]
    long DateEpoch,
    [property: JsonPropertyName("likes")]
    int Likes,
    [property: JsonPropertyName("replies")]
    int Replies,
    [property: JsonPropertyName("retweets")]
    int Retweets,
    [property: JsonPropertyName("text")]
    string Text,
    [property: JsonPropertyName("user_name")]
    string UserName,
    [property: JsonPropertyName("user_screen_name")]
    string UserScreenName,
    [property: JsonPropertyName("media_extended")]
    List<VxTwitterMedia> Media
 );

public sealed record VxTwitterMedia(
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("url")]
    string Url
);

#endregion
