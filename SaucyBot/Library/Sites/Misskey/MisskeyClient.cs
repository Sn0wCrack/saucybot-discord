using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.Misskey;

public sealed class MisskeyClient : IMisskeyClient
{
    private const string DefaultBaseUrl = "https://misskey.io";
    
    private readonly ICacheManager _cache;
    
    private readonly HttpClient _client = new();

    private string BaseUrl = DefaultBaseUrl;
    
    public MisskeyClient(ICacheManager cacheManager)
    {
        _cache = cacheManager;
        
        _client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SaucyBot", Assembly.GetEntryAssembly()?.GetName().Version?.ToString())    
        );
        
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }

    public async Task<ShowNoteResponse?> ShowNote(string id)
    {
        var response = await _cache.Remember($"misskey.{BaseUrl}.note_{id}", async () =>
        {
            var request = JsonContent.Create(new { noteId = id });
            
            var response = await _client.PostAsync($"{BaseUrl}/api/notes/show", request);

            return await response.Content.ReadAsStringAsync();
        });

        return response is null ? null : JsonSerializer.Deserialize<ShowNoteResponse>(response);
    }

    public void SetUrl(string url)
    {
        BaseUrl = url.TrimEnd('/');
    }
}

#region Response Types
public sealed record ShowNoteResponse(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("createdAt")]
    string CreatedAt,
    [property: JsonPropertyName("userId")]
    string UserId,
    [property: JsonPropertyName("text")]
    string? Text,
    [property: JsonPropertyName("visibility")]
    string Visibility,
    [property: JsonPropertyName("files")]
    List<MisskeyFile> Files,
    [property: JsonPropertyName("user")]
    MisskeyUser User
);

public sealed record MisskeyUser(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("username")]
    string Username,
    [property: JsonPropertyName("avatarUrl")]
    string AvatarUrl
);

public sealed record MisskeyFile(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("createdAt")]
    string CreatedAt,
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("size")]
    int Size,
    [property: JsonPropertyName("isSensitive")]
    bool IsSensitive,
    [property: JsonPropertyName("url")]
    string Url,
    [property: JsonPropertyName("thumbnailUrl")]
    string ThumbnailUrl
);
#endregion
