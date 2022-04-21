using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.FurAffinity;

public class FaExportClient
{
    private const string BaseUrl = "https://faexport.spangle.org.uk";

    private readonly CacheManager _cache;
    
    private readonly HttpClient _client = new();

    public FaExportClient(CacheManager cacheManager)
    {
        _cache = cacheManager;
        
        _client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SaucyBot", Assembly.GetEntryAssembly()?.GetName().Version?.ToString())    
        );
        
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }

    public async Task<FaExportSubmission?> GetSubmission(string identifier)
    {
        var response = await _cache.Remember($"furaffinity.post_{identifier}", async () => await _client.GetStringAsync($"{BaseUrl}/submission/{identifier}.json"));

        return response is null ? null : JsonSerializer.Deserialize<FaExportSubmission>(response);
    }
}

#region Response Types
public record FaExportSubmission(
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
