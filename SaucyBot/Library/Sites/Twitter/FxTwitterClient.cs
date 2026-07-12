using System.Net;
using System.Net.Http.Headers;
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

    private readonly HttpClient _client;

    private readonly ResiliencePipeline<string?> _pipeline;

    public FxTwitterClient(ILogger<FxTwitterClient> logger, ICacheManager cacheManager, HttpClient client)
    {
        _logger = logger;
        _cache = cacheManager;
        _client = client;

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

    public async Task<FxTwitterResponse?> GetTweet(string name, string identifier, string? translate = null)
    {
        var response = await _cache.Remember(
            BuildCacheKey(name, identifier, translate),
            async () => await _pipeline.ExecuteAsync(async token => await _client.GetStringAsync(BuildUrl(name, identifier, translate), token))
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

    private static string BuildUrl(string name, string identifier, string? translate = null)
    {
        return translate is null or "original"
            ? $"{BaseUrl}/{name}/status/{identifier}"
            : $"{BaseUrl}/{name}/status/{identifier}/{translate}";
    }

    private static string BuildCacheKey(string name, string identifier, string? translate = null)
    {
        return translate is null
            ? $"fxtwitter.tweet_{name}_{identifier}"
            : $"fxtwitter.tweet_{name}_{identifier}_{translate}";
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
    int? Replies,
    [property: JsonPropertyName("retweets")]
    int? Retweets,
    [property: JsonPropertyName("likes")]
    int? Likes,
    [property: JsonPropertyName("views")]
    int? Views,
    [property: JsonPropertyName("bookmarks")]
    int? Bookmarks,
    [property: JsonPropertyName("color")]
    string? Color,
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
    [property: JsonPropertyName("translation")]
    FxTwitterTranslation? Translation,
    [property: JsonPropertyName("quote")]
    FxTwitterTweet? QuotedTweet,
    [property: JsonPropertyName("poll")]
    FxTwitterTweet? Poll,
    [property: JsonPropertyName("media")]
    FxTwitterMedia? Media
);

public sealed record FxTwitterAuthor(
    [property: JsonPropertyName("id")]
    string Id,
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

public sealed record FxTwitterTranslation(
    [property: JsonPropertyName("text")]
    string Text,
    [property: JsonPropertyName("source_lang")]
    string SourceLanguage,
    [property: JsonPropertyName("target_lang")]
    string TargetLanguage
);

#endregion
