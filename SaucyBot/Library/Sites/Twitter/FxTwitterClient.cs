using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Polly;
using Polly.Fallback;
using Polly.Retry;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.Twitter;

public sealed class FxTwitterClient : IFxTwitterClient
{
    private const string BaseUrl = "https://api.fxtwitter.com";

    private readonly ILogger<FxTwitterClient> _logger;
    
    private readonly ICacheManager _cache;
    
    private readonly HttpClient _client = new();
    
    private readonly ResiliencePipeline<string?> _pipeline;

    public FxTwitterClient(ILogger<FxTwitterClient> logger, ICacheManager cacheManager)
    {
        _logger = logger;
        _cache = cacheManager;
        
        _client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SaucyBot", Assembly.GetEntryAssembly()?.GetName().Version?.ToString())    
        );
        
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
        
        _pipeline = new ResiliencePipelineBuilder<string?>()
            .AddFallback(new FallbackStrategyOptions<string?>
            {
                FallbackAction = _ => Outcome.FromResultAsValueTask<string?>(null),
                ShouldHandle = arguments => arguments.Outcome switch
                {
                    { Exception: HttpRequestException e } => e.StatusCode == HttpStatusCode.NotFound ? PredicateResult.True() : PredicateResult.False(),
                    _ => PredicateResult.False(), 
                }
            })
            .AddRetry(new RetryStrategyOptions<string?>
            {
                ShouldHandle = arguments => arguments.Outcome switch
                {
                    { Exception: HttpRequestException e } => e.StatusCode >= HttpStatusCode.InternalServerError ? PredicateResult.True() : PredicateResult.False(),
                    _ => PredicateResult.False(),
                },
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(3)
            })
            .AddTimeout(TimeSpan.FromSeconds(15))
            .Build();
    }
    
    public async Task<FxTwitterResponse?> GetTweet(string name, string identifier)
    {
        var response = await _cache.Remember(
            $"fxtwitter.tweet_{name}_{identifier}",
            async () => await _pipeline.ExecuteAsync(async token => await _client.GetStringAsync($"{BaseUrl}/{name}/status/{identifier}", token))
        );

        if (response is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FxTwitterResponse>(response);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Failed to deserialize FxTwitter response, response not JSON or is malformed.");
            return null;
        } 
    }
}

#region Response Types

public sealed record FxTwitterResponse(
    [property: JsonPropertyName("code")]
    int Code,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("tweet")]
    FxTwitterTweet Tweet
);


public sealed record FxTwitterTweet(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("url")]
    string? Url,
    [property: JsonPropertyName("text")]
    string Text,
    [property: JsonPropertyName("created_at")]
    string CreatedAt,
    [property: JsonPropertyName("created_timestamp")]
    long CreatedTimestamp,
    [property: JsonPropertyName("author")]
    FxTwitterAuthor Author,
    [property: JsonPropertyName("replies")]
    int Replies,
    [property: JsonPropertyName("retweets")]
    int Retweets,
    [property: JsonPropertyName("likes")]
    int Likes,
    [property: JsonPropertyName("views")]
    int? Views,
    [property: JsonPropertyName("color")]
    string Color,
    [property: JsonPropertyName("twitter_card")]
    string TwitterCard,
    [property: JsonPropertyName("lang")]
    string? Language,
    [property: JsonPropertyName("source")]
    string Source,
    [property: JsonPropertyName("possibly_sensitive")]
    bool PossiblySensitive,
    [property: JsonPropertyName("replying_to")]
    string? ReplyingToScreenName,
    [property: JsonPropertyName("replying_to_status")]
    string? ReplyingToStatusId,
    [property: JsonPropertyName("quote")]
    FxTwitterTweet? QuotedTweet,
    [property: JsonPropertyName("poll")]
    FxTwitterTweet? Poll,
    [property: JsonPropertyName("media")]
    FxTwitterMedia? Media
);

public sealed record FxTwitterAuthor(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("screen_name")]
    string ScreenName,
    [property: JsonPropertyName("avatar_url")]
    string? AvatarUrl,
    [property: JsonPropertyName("url")]
    string? Url,
    [property: JsonPropertyName("avatar_color")]
    string? AvatarColor,
    [property: JsonPropertyName("banner_url")]
    string? BannerUrl
);

public sealed record FxTwitterMedia(
    [property: JsonPropertyName("photos")]
    List<FxTwitterPhoto>? Photos,
    [property: JsonPropertyName("videos")]
    List<FxTwitterVideo>? Videos
);

public sealed record FxTwitterVideo(
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("url")]
    string Url,
    [property: JsonPropertyName("thumbnail_url")]
    string ThumbnailUrl,
    [property: JsonPropertyName("width")]
    int Width,
    [property: JsonPropertyName("height")]
    int Height,
    [property: JsonPropertyName("format")]
    string Format
);

public sealed record FxTwitterPhoto(
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("url")]
    string Url,
    [property: JsonPropertyName("width")]
    int Width,
    [property: JsonPropertyName("height")]
    int Height
);

public sealed record FxTwitterPoll(
    [property: JsonPropertyName("choises")]
    List<FxTwitterPollChoice> Choices,
    [property: JsonPropertyName("total_votes")]
    uint TotalVotes,
    [property: JsonPropertyName("ends_at")]
    string EndsAt,
    [property: JsonPropertyName("time_left_en")]
    string TimeLeft
);

public sealed record FxTwitterPollChoice(
    [property: JsonPropertyName("label")]
    string Label,
    [property: JsonPropertyName("count")]
    int Count,
    [property: JsonPropertyName("percentage")]
    int Percentage
);

#endregion
