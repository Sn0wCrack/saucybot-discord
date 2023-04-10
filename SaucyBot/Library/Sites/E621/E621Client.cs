using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.E621;

public class E621Client : IE621Client
{
    private const string BaseUrl = "https://e621.net";

    private readonly ICacheManager _cache;
    
    private readonly HttpClient _client = new();
    
    public E621Client(ICacheManager cacheManager)
    {
        _cache = cacheManager;
        
        _client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SaucyBot", Assembly.GetEntryAssembly()?.GetName().Version?.ToString())    
        );
        
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }

    public async Task<E621PostResponse?> GetPost(string identifier)
    {
        var response = await _cache.Remember($"e621.post_{identifier}",
            async () => await _client.GetStringAsync($"{BaseUrl}/posts/{identifier}.json"));

        return response is null ? null : JsonSerializer.Deserialize<E621PostResponse>(response);
    }
}

#region Reponse Types

public record E621PostResponse(
    [property: JsonPropertyName("post")]
    E621Post Post
);

public record E621Post(
    [property: JsonPropertyName("id")]
    int Id,
    [property: JsonPropertyName("created_at")]
    string CreatedAt,
    [property: JsonPropertyName("updated_at")]
    string UpdatedAt,
    [property: JsonPropertyName("file")]
    E621PostFile File,
    [property: JsonPropertyName("preview")]
    E621PostPreview Preview,
    [property: JsonPropertyName("sample")]
    E621PostSample Sample,
    [property: JsonPropertyName("score")]
    E621PostScore Score,
    [property: JsonPropertyName("tags")]
    E621PostTags Tags,
    [property: JsonPropertyName("description")]
    string Description
);

public record E621PostFile(
    [property: JsonPropertyName("width")]
    int Width,
    [property: JsonPropertyName("height")]
    int Height,
    [property: JsonPropertyName("ext")]
    string Extension,
    [property: JsonPropertyName("size")]
    int Size,
    [property: JsonPropertyName("md5")]
    string Hash,
    [property: JsonPropertyName("url")]
    string Url
);

public record E621PostPreview(
    [property: JsonPropertyName("width")]
    int Width,
    [property: JsonPropertyName("height")]
    int Height,
    [property: JsonPropertyName("url")]
    string Url
);

public record E621PostSample(
    [property: JsonPropertyName("has")]
    bool Has,
    [property: JsonPropertyName("width")]
    int Width,
    [property: JsonPropertyName("height")]
    int Height,
    [property: JsonPropertyName("url")]
    string Url
);

public record E621PostTags(
    [property: JsonPropertyName("artist")]
    string[] Artist,
    [property: JsonPropertyName("meta")]
    string[] Meta
);

public record E621PostScore(
    [property: JsonPropertyName("up")]
    int Up,
    [property: JsonPropertyName("down")]
    int Down,
    [property: JsonPropertyName("total")]
    int Total
);
#endregion
