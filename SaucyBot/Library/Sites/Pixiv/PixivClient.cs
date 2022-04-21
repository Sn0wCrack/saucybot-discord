using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.Pixiv;

public class PixivClient
{
    private const string BaseUrl = "https://www.pixiv.net";
    private const string LoginPageUrl = "https://accounts.pixiv.net/login";
    private const string LoginApiUrl = "https://accounts.pixiv.net/api/login";
    private const string WebApiUrl = "https://www.pixiv.net/ajax";

    private readonly Uri _referrer = new(BaseUrl);

    private readonly ILogger<PixivClient> _logger;
    private readonly IConfiguration _configuration;
    private readonly CacheManager _cache;

    private readonly CookieContainer _cookieContainer = new();
    private readonly HttpClient _client;

    public PixivClient(
        ILogger<PixivClient> logger,
        IConfiguration configuration,
        CacheManager cacheManager
    ) {
        _logger = logger;
        _configuration = configuration;
        _cache = cacheManager;
        
        _cookieContainer.Add(new Cookie
        {
            Name = "PHPSESSID",
            Value = _configuration.GetSection("Sites:Pixiv:SessionCookie").Get<string>(),
            Domain = "pixiv.net",
            Path = "/",
            HttpOnly = false,
            Secure = false
        });

        var httpClientHandler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = true,
        };

        _client = new HttpClient(httpClientHandler);

        _client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.103 Safari/537.36"
        );

        _client.DefaultRequestHeaders.Referrer = _referrer;
        
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }

    public async Task<bool> Login()
    {
        return await CookieLogin();
    }

    private async Task<bool> CookieLogin()
    {
        var response = await _client.GetStringAsync(BaseUrl);

        return response.Contains("logout.php") ||
                     response.Contains("pixiv.user.loggedIn = true") ||
                     response.Contains("_gaq.push(['_setCustomVar', 1, 'login', 'yes'") ||
                     response.Contains("var dataLayer = [{ login: 'yes',");
    }

    public async Task<IllustrationDetailsResponse?> IllustrationDetails(string id)
    {
        var response = await _cache.Remember($"pixiv.illustration_details_{id}", async () => await _client.GetStringAsync($"{WebApiUrl}/illust/{id}"));

        return response is null ? null : JsonSerializer.Deserialize<IllustrationDetailsResponse>(response);
    }

    public async Task<IllustrationPagesResponse?> IllustrationPages(string id)
    {
        var response = await _cache.Remember($"pixiv.illustration_pages_{id}", async () => await _client.GetStringAsync($"{WebApiUrl}/illust/{id}/pages")); 
        
        return response is null ? null : JsonSerializer.Deserialize<IllustrationPagesResponse>(response);
    }

    public async Task<UgoiraMetadataResponse?> UgoiraMetadata(string id)
    {
        var response = await _cache.Remember($"pixiv.ugoira_metadata_{id}", async () => await _client.GetStringAsync($"{WebApiUrl}/illust/{id}/ugoira_meta"));
        
        return response is null ? null : JsonSerializer.Deserialize<UgoiraMetadataResponse>(response);
    }

    public async Task<HttpResponseMessage> PokeFile(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        
        return await _client.SendAsync(request);
    }

    public async Task<MemoryStream> GetFile(string url)
    {
        var response = await _client.GetStreamAsync(url);

        var stream = new MemoryStream();
        
        await response.CopyToAsync(stream);

        return stream;
    }
}

#region Response Types
public record IllustrationDetailsResponse(
    [property: JsonPropertyName("error")]
    bool Error,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("body")]
    IllustrationDetails IllustrationDetails
);

public enum IllustrationType
{
    Illustration = 0,
    // Illustration Type 1 seems to be the same as Type 0
    // These might be from pixiv Sketch potentially?
    Unknown = 1,
    Ugoira = 2,
}

public record IllustrationDetails(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("title")]
    string Title,
    [property: JsonPropertyName("description")]
    string Description,
    [property: JsonPropertyName("illustType")]
    IllustrationType Type,
    [property: JsonPropertyName("urls")]
    IllustrationDetailsUrls IllustrationDetailsUrls,
    [property: JsonPropertyName("pageCount")]
    int PageCount
);

public record IllustrationDetailsUrls(
    [property: JsonPropertyName("mini")]
    string Mini,
    [property: JsonPropertyName("thumb")]
    string Thumbnail,
    [property: JsonPropertyName("small")]
    string Small,
    [property: JsonPropertyName("regular")]
    string Regular,
    [property: JsonPropertyName("original")]
    string Original
)
{
    public string[] All => new[] { Original, Regular, Small, Thumbnail, Mini };
};

public record IllustrationPagesResponse(
    [property: JsonPropertyName("error")]
    bool Error,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("body")]
    List<IllustrationPages> IllustrationPages
);

public record IllustrationPages(
    [property: JsonPropertyName("urls")]
    IllustrationPagesUrls IllustrationPagesUrls,
    [property: JsonPropertyName("width")]
    int Width,
    [property: JsonPropertyName("height")]
    int Height
);

public record IllustrationPagesUrls(
    [property: JsonPropertyName("thumb_mini")]
    string Thumbnail,
    [property: JsonPropertyName("small")]
    string Small,
    [property: JsonPropertyName("regular")]
    string Regular,
    [property: JsonPropertyName("original")]
    string Original
)
{
    public string[] All => new[] { Original, Regular, Small, Thumbnail };
};

public record UgoiraMetadataResponse(
    [property: JsonPropertyName("error")]
    bool Error,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("body")]
    UgoiraMetadata UgoiraMetadata
);

public record UgoiraMetadata(
    [property: JsonPropertyName("frames")]
    List<UgoiraFrame> Frames,
    [property: JsonPropertyName("mime_type")]
    string MimeType,
    [property: JsonPropertyName("originalSrc")]
    string OriginalSource,
    [property: JsonPropertyName("src")]
    string Source
);

public record UgoiraFrame(
    [property: JsonPropertyName("file")]
    string File,
    [property: JsonPropertyName("delay")]
    int Delay
);

#endregion
