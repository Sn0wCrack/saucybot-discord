using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Polly;
using Polly.Fallback;
using Polly.Retry;
using Polly.Timeout;
using SaucyBot.Common;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.Twitter;

public sealed class VxTwitterClient : IVxTwitterClient
{
    private const string BaseUrl = "https://api.vxtwitter.com";

    private readonly ILogger<VxTwitterClient> _logger;
    private readonly ICacheManager _cache;
    private readonly HttpClient _client;
    private readonly ResiliencePipeline<string?> _pipeline;

    public VxTwitterClient(ILogger<VxTwitterClient> logger, ICacheManager cacheManager, HttpClient client)
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
                    { Exception: HttpRequestException e } => e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound
                        ? PredicateResult.True()
                        : PredicateResult.False(),
                    _ => PredicateResult.False(),
                }
            })
            .AddRetry(new RetryStrategyOptions<string?>
            {
                ShouldHandle = arguments => arguments.Outcome switch
                {
                    { Exception: HttpRequestException e } => e.StatusCode >= HttpStatusCode.InternalServerError
                        ? PredicateResult.True()
                        : PredicateResult.False(),
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

    public async Task<VxTwitterResponse?> GetTweet(
        string name,
        string identifier,
        string? includeRtf = null,
        string? includeTxt = null)
    {
        string? response;
        try
        {
            response = await _cache.Remember(
                BuildCacheKey(name, identifier, includeRtf, includeTxt),
                async () => await _pipeline.ExecuteAsync(async token => await _client.GetStringAsync(
                    BuildUrl(name, identifier, includeRtf, includeTxt),
                    token))
            );
        }
        catch (HttpRequestException e)
        {
            _logger.LogDebug(e, "Failed to retrieve VxTwitter response.");
            return null;
        }
        catch (TimeoutRejectedException e)
        {
            _logger.LogDebug(e, "Timed out while retrieving VxTwitter response.");
            return null;
        }

        if (response is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<VxTwitterResponse>(response);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Failed to deserialize VxTwitter response, response not JSON or is malformed.");
            return null;
        }
    }

    private static string BuildUrl(string name, string identifier, string? includeRtf, string? includeTxt)
    {
        var query = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(includeRtf))
        {
            query["include_rtf"] = includeRtf;
        }

        if (!string.IsNullOrWhiteSpace(includeTxt))
        {
            query["include_txt"] = includeTxt;
        }

        return Helper.GetUriWithQueryString($"{BaseUrl}/{name}/status/{identifier}", query)!;
    }

    private static string BuildCacheKey(string name, string identifier, string? includeRtf, string? includeTxt)
    {
        return $"vxtwitter.tweet_{name}_{identifier}_{includeRtf ?? "default"}_{includeTxt ?? "default"}";
    }
}

#region Response Types

public sealed record VxTwitterResponse(
    [property: JsonPropertyName("date")]
    string Date,
    [property: JsonPropertyName("date_epoch")]
    long DateEpoch,
    [property: JsonPropertyName("hashtags")]
    List<string> Hashtags,
    [property: JsonPropertyName("likes")]
    int Likes,
    [property: JsonPropertyName("mediaURLs")]
    List<string> MediaUrls,
    [property: JsonPropertyName("media_extended")]
    List<VxTwitterMedia> MediaExtended,
    [property: JsonPropertyName("replies")]
    int Replies,
    [property: JsonPropertyName("retweets")]
    int Retweets,
    [property: JsonPropertyName("text")]
    string Text,
    [property: JsonPropertyName("tweetID")]
    string TweetId,
    [property: JsonPropertyName("tweetURL")]
    string TweetUrl,
    [property: JsonPropertyName("user_name")]
    string UserName,
    [property: JsonPropertyName("user_screen_name")]
    string UserScreenName,
    [property: JsonPropertyName("user_profile_image_url")]
    string UserProfileImageUrl,
    [property: JsonPropertyName("qrt")]
    VxTwitterResponse? QuotedTweet
);

public sealed record VxTwitterMedia(
    [property: JsonPropertyName("altText")]
    string? AltText,
    [property: JsonPropertyName("duration_millis")]
    long? DurationMillis,
    [property: JsonPropertyName("size")]
    VxTwitterMediaSize Size,
    [property: JsonPropertyName("thumbnail_url")]
    string ThumbnailUrl,
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("url")]
    string Url
);

public sealed record VxTwitterMediaSize(
    [property: JsonPropertyName("height")]
    int Height,
    [property: JsonPropertyName("width")]
    int Width
);

#endregion
