using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.ArtStation;

public class ArtStationClient
{
    private const string BaseUrl = "https://www.artstation.com";
    
    private readonly ILogger<ArtStationClient> _logger;
    private readonly CacheManager _cache;
    
    private readonly HttpClient _client = new();

    public ArtStationClient(ILogger<ArtStationClient> logger, CacheManager cacheManager)
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

    public async Task<Project?> GetProject(string hash)
    {
        var response = await _cache.Remember($"artstation.project_{hash}", async () => await _client.GetStringAsync($"{BaseUrl}/projects/{hash}.json"));

        return response is null ? null : JsonSerializer.Deserialize<Project>(response);
    }
}

#region Response Types

public record Project(
    [property: JsonPropertyName("id")]
    ulong Id,
    [property: JsonPropertyName("title")]
    string Title,
    [property: JsonPropertyName("description")]
    string Description,
    [property: JsonPropertyName("cover_url")]
    string CoverUrl,
    [property: JsonPropertyName("permalink")]
    string Permalink,
    [property: JsonPropertyName("likes_count")]
    uint LikesCount,
    [property: JsonPropertyName("views_count")]
    uint ViewsCount,
    [property: JsonPropertyName("published_at")]
    string PublishedAt,
    [property: JsonPropertyName("user")]
    ProjectUser User,
    [property: JsonPropertyName("assets")]
    List<ProjectAsset> Assets
);

public record ProjectUser(
    [property: JsonPropertyName("username")]
    string Username,
    [property: JsonPropertyName("full_name")]
    string FullName,
    [property: JsonPropertyName("permalink")]
    string Permalink,
    [property: JsonPropertyName("large_avatar_url")]
    string LargeAvatarUrl,
    [property: JsonPropertyName("medium_avatar_url")]
    string MediumAvatarUrl,
    [property: JsonPropertyName("small_cover_url")]
    string SmallCoverUrl
);

public record ProjectAsset(
    [property: JsonPropertyName("id")]
    ulong Id,
    [property: JsonPropertyName("title")]
    string? Title,
    [property: JsonPropertyName("asset_type")]
    string Type,
    [property: JsonPropertyName("image_url")]
    string ImageUrl,
    [property: JsonPropertyName("width")]
    uint Width,
    [property: JsonPropertyName("height")]
    uint Height,
    [property: JsonPropertyName("position")]
    int Position,
    [property: JsonPropertyName("has_image")]
    bool HasImage,
    [property: JsonPropertyName("has_embedded_player")]
    bool HasEmbeddedPlayer
);

#endregion
