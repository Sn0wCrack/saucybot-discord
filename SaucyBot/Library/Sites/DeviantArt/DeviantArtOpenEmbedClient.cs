﻿using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaucyBot.Extensions;
using SaucyBot.Services;


namespace SaucyBot.Library.Sites.DeviantArt;

public sealed class DeviantArtOpenEmbedClient : IDeviantArtOpenEmbedClient
{
    private const string EndpointUrl = "https://backend.deviantart.com/oembed";

    private readonly ICacheManager _cache;
    
    private readonly HttpClient _client = new();

    public DeviantArtOpenEmbedClient(ICacheManager cacheManager)
    {
        _cache = cacheManager;
        
        _client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SaucyBot", Assembly.GetEntryAssembly()?.GetName().Version?.ToString())    
        );
        
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }

    public async Task<OpenEmbedResponse?> Get(string url)
    {
        var query = new Dictionary<string, string> { ["url"] = url };
        
        var response = await _cache.Remember($"deviantart.oembed_{url}", async () => await _client.GetStringWithQueryStringAsync(EndpointUrl, query));

        return response is null ? null : JsonSerializer.Deserialize<OpenEmbedResponse>(response);
    }
}

#region Response Types

public sealed record OpenEmbedResponse(
    [property: JsonPropertyName("version")]
    string Version,
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("title")]
    string Title,
    [property: JsonPropertyName("url")]
    string Url,
    [property: JsonPropertyName("author_name")]
    string AuthorName,
    [property: JsonPropertyName("author_url")]
    string AuthorUrl,
    [property: JsonPropertyName("provider_name")]
    string ProviderName,
    [property: JsonPropertyName("provider_url")]
    string ProviderUrl,
    [property: JsonPropertyName("thumbnail_url")]
    string ThumbnailUrl
);
#endregion
