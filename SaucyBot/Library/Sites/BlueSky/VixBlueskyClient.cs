using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Polly;
using Polly.Fallback;
using Polly.Retry;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.BlueSky;

public class VixBlueskyClient : IVixBlueskyClient
{
    private const string BaseUrl = "https://bskx.app";

    private readonly ILogger<VixBlueskyClient> _logger;

    private readonly ICacheManager _cache;

    private readonly HttpClient _client;

    private readonly ResiliencePipeline<string?> _pipeline;

    public VixBlueskyClient(ILogger<VixBlueskyClient> logger, ICacheManager cacheManager, HttpClient client)
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

    public async Task<VixBlueskyResponse?> GetPost(string name, string identifier)
    {
        var response = await _cache.Remember(
            $"vixbluesky .post_{name}_{identifier}",
            async () => await _pipeline.ExecuteAsync(async token => await _client.GetStringAsync($"{BaseUrl}/profile/{name}/post/{identifier}/json", token))
        );

        if (response is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<VixBlueskyResponse>(response);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Failed to deserialize VixBluesky response, response not JSON or is malformed.");
            return null;
        }
    }
}

#region Response Types

public sealed record VixBlueskyResponse(
    [property: JsonPropertyName("posts")]
    List<VixBlueskyPost> Posts
);

public sealed record VixBlueskyPost(
    [property: JsonPropertyName("author")]
    VixBlueskyUser Author,
    [property: JsonPropertyName("record")]
    VixBlueskyRecord Record,
    [property: JsonPropertyName("embed")]
    VixBlueskyEmbed? Embed,
    [property: JsonPropertyName("embeds")]
    List<VixBlueskyEmbed>? Embeds,
    [property: JsonPropertyName("replyCount")]
    int Replies,
    [property: JsonPropertyName("repostCount")]
    int Reposts,
    [property: JsonPropertyName("likeCount")]
    int Likes,
    [property: JsonPropertyName("quoteCount")]
    int Quotes
);

public sealed record VixBlueskyUser(
    [property: JsonPropertyName("handle")]
    string Handle,
    [property: JsonPropertyName("displayName")]
    string DisplayName,
    [property: JsonPropertyName("avatar")]
    string AvatarUrl
);

public sealed record VixBlueskyRecord(
    [property: JsonPropertyName("$type")]
    string Type,
    [property: JsonPropertyName("createdAt")]
    string CreatedAt,
    [property: JsonPropertyName("text")]
    string Text,
    [property: JsonPropertyName("embed")]
    VixBlueskyRecordEmbed? Embed
);

public sealed record VixBlueskyRecordEmbed(
    [property: JsonPropertyName("$type")]
    string Type
);

public sealed record VixBlueskyEmbed(
    [property: JsonPropertyName("$type")]
    string Type,
    // Available on type: app.bsky.embed.video#view
    [property: JsonPropertyName("playlist")]
    string? Playlist,
    // Available on type: app.bsky.embed.images#view
    [property: JsonPropertyName("images")]
    List<VixBlueskyEmbedImage>? Images,
    // Available on type: app.bsky.embed.recordWithMedia#view
    [property: JsonPropertyName("media")]
    VixBlueskyEmbedMedia? Media,
    // Available on type: app.bsky.embed.record#view and app.bsky.embed.recordWithMedia#view
    [property: JsonPropertyName("record")]
    VixBlueskyPost? Record
);

public sealed record VixBlueskyEmbedMedia(
    [property: JsonPropertyName("images")]
    List<VixBlueskyEmbedImage>? Images
);

public sealed record VixBlueskyEmbedImage(
    [property: JsonPropertyName("thumbnail")]
    string ThumbnailUrl,
    [property: JsonPropertyName("fullsize")]
    string Url
);

#endregion
