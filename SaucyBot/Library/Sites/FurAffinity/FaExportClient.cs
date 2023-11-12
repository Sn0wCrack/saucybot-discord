using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Polly;
using Polly.Fallback;
using Polly.Retry;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.FurAffinity;

public sealed class FaExportClient : IFurAffinityClient
{
    private const string BaseUrl = "https://faexport.spangle.org.uk";

    private readonly ICacheManager _cache;
    
    private readonly HttpClient _client = new();

    private readonly ResiliencePipeline<string?> _pipeline;

    public FaExportClient(ICacheManager cacheManager)
    {
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

    public async Task<FaExportSubmission?> GetSubmission(string identifier)
    {
        var response = await _cache.Remember($"furaffinity.post_{identifier}", async () =>
        {
            return await _pipeline.ExecuteAsync(async token => await _client.GetStringAsync($"{BaseUrl}/submission/{identifier}.json", token));
        });

        return response is null ? null : JsonSerializer.Deserialize<FaExportSubmission>(response);
    }
}

#region Response Types
public sealed record FaExportSubmission(
    [property: JsonPropertyName("title")]
    string Title,
    [property: JsonPropertyName("description")]
    string Description,
    [property: JsonPropertyName("description_body")]
    string DescriptionBody,
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("profile")]
    string Profile,
    [property: JsonPropertyName("profile_name")]
    string ProfileName,
    [property: JsonPropertyName("avatar")]
    string Avatar,
    [property: JsonPropertyName("link")]
    string Link,
    [property: JsonPropertyName("posted")]
    string Posted,
    [property: JsonPropertyName("posted_at")]
    string PostedAt,
    [property: JsonPropertyName("download")]
    string Download,
    [property: JsonPropertyName("full")]
    string Full,
    [property: JsonPropertyName("thumbnail")]
    string Thumbnail,
    [property: JsonPropertyName("category")]
    string Category,
    [property: JsonPropertyName("theme")]
    string Theme,
    [property: JsonPropertyName("species")]
    string Species,
    [property: JsonPropertyName("gender")]
    string Gender,
    [property: JsonPropertyName("favorites")]
    string Favorites,
    [property: JsonPropertyName("comments")]
    string Comments,
    [property: JsonPropertyName("views")]
    string Views,
    [property: JsonPropertyName("resolution")]
    string Resolution,
    [property: JsonPropertyName("rating")]
    string Rating,
    [property: JsonPropertyName("keywords")]
    string[] Keywords
);
#endregion
