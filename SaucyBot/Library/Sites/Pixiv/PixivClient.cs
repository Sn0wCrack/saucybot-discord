using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Polly;
using Polly.Fallback;
using Polly.Retry;
using SaucyBot.Common;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.Pixiv;

public sealed class PixivClient : IPixivClient
{
    private const string BaseUrl = "https://www.pixiv.net";
    private const string LoginPageUrl = "https://accounts.pixiv.net/login";
    private const string LoginApiUrl = "https://accounts.pixiv.net/api/login";
    private const string WebApiUrl = "https://www.pixiv.net/ajax";

    private readonly ILogger<PixivClient> _logger;
    private readonly IConfiguration _configuration;
    private readonly ICacheManager _cache;

    private readonly HttpClient _client;

    private readonly ResiliencePipeline<string?> _pipeline;

    private bool _isLoggedIn;

    public PixivClient(
        ILogger<PixivClient> logger,
        IConfiguration configuration,
        ICacheManager cacheManager,
        HttpClient client
    ) {
        _logger = logger;
        _configuration = configuration;
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

    public async Task<bool> Login()
    {
        if (_isLoggedIn)
        {
            return true;
        }
        
        return _isLoggedIn = await CookieLogin();
    }

    private async Task<bool> CookieLogin()
    {
        try
        {
            var response = await _pipeline.ExecuteAsync(async token => await _client.GetStringAsync(BaseUrl, token));

            if (response is null)
            {
                return false;
            }

            return response.Contains("logout.php") ||
                   response.Contains("pixiv.user.loggedIn = true") ||
                   response.Contains("_gaq.push(['_setCustomVar', 1, 'login', 'yes'") ||
                   response.Contains("var dataLayer = [{ login: 'yes',");
        }
        catch (Exception e)
        {
            _logger.LogDebug("Failed logging into Pixiv with error: {Exception}", e.Message);
            
            return false;
        }
    }

    public async Task<IllustrationDetailsResponse?> IllustrationDetails(string id)
    {
        var response = await _cache.Remember($"pixiv.illustration_details_{id}", async () =>
            await _pipeline.ExecuteAsync(async token => await _client.GetStringAsync($"{WebApiUrl}/illust/{id}", token))
        );

        return response is null ? null : JsonSerializer.Deserialize<IllustrationDetailsResponse>(response);
    }

    public async Task<IllustrationPagesResponse?> IllustrationPages(string id)
    {
        var response = await _cache.Remember($"pixiv.illustration_pages_{id}", async () =>
            await _pipeline.ExecuteAsync(async token => await _client.GetStringAsync($"{WebApiUrl}/illust/{id}/pages", token))
        );
        
        return response is null ? null : JsonSerializer.Deserialize<IllustrationPagesResponse>(response);
    }

    public async Task<UgoiraMetadataResponse?> UgoiraMetadata(string id)
    {
        var response = await _cache.Remember($"pixiv.ugoira_metadata_{id}", async () =>
            await _pipeline.ExecuteAsync(async token => await _client.GetStringAsync($"{WebApiUrl}/illust/{id}/ugoira_meta", token))
        );
        
        return response is null ? null : JsonSerializer.Deserialize<UgoiraMetadataResponse>(response);
    }
    
    public async Task<UserDetailsResponse?> UserDetails(string id)
    {
        var response = await _cache.Remember($"pixiv.user_{id}", TimeSpan.FromDays(7), async () =>
            await _pipeline.ExecuteAsync(async token => await _client.GetStringAsync($"{WebApiUrl}/user/{id}", token))
        );
        
        return response is null ? null : JsonSerializer.Deserialize<UserDetailsResponse>(response);
    }

    public async Task<HttpResponseMessage> PokeFile(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        
        return await _client.SendAsync(request);
    }

    public async Task<Stream> GetFile(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        var contentLength = response.Content.Headers.ContentLength ?? -1;
        var stream = await response.Content.ReadAsStreamAsync();

        return new KnownLengthStream(stream, contentLength);
    }
}

#region Response Types
public sealed record IllustrationDetailsResponse(
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
    // Illustration Type 1 seems to be the same as Type 0.
    // These might be from pixiv Sketch potentially?
    Unknown = 1,
    Ugoira = 2,
}

public sealed record IllustrationDetails(
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
    int PageCount,
    [property: JsonPropertyName("userId")]
    string UserId,
    [property: JsonPropertyName("userName")]
    string UserName,
    [property: JsonPropertyName("userAccount")]
    string UserAccount
)
{
    public string Url => $"https://www.pixiv.net/en/artworks/{Id}";

    public string UserUrl => $"https://www.pixiv.net/en/users/{UserId}";
};

public sealed record IllustrationDetailsUrls(
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
    public IEnumerable<string> All => [Original, Regular, Small, Thumbnail, Mini];
    public IEnumerable<string> AllWithoutThumbnails => [Original, Regular, Small];
    public IEnumerable<string> AllWithoutOriginalAndThumbnails => [Regular, Small];
};

public record IllustrationPagesResponse(
    [property: JsonPropertyName("error")]
    bool Error,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("body")]
    List<IllustrationPages> IllustrationPages
);

public sealed record IllustrationPages(
    [property: JsonPropertyName("urls")]
    IllustrationPagesUrls IllustrationPagesUrls,
    [property: JsonPropertyName("width")]
    int Width,
    [property: JsonPropertyName("height")]
    int Height
);

public sealed record IllustrationPagesUrls(
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
    public IEnumerable<string> All => [Original, Regular, Small, Thumbnail];
    public IEnumerable<string> AllWithoutThumbnails => [Original, Regular, Small];
    public IEnumerable<string> AllWithoutOriginalAndThumbnails => [Regular, Small];
};

public sealed record UgoiraMetadataResponse(
    [property: JsonPropertyName("error")]
    bool Error,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("body")]
    UgoiraMetadata UgoiraMetadata
);

public sealed record UgoiraMetadata(
    [property: JsonPropertyName("frames")]
    List<UgoiraFrame> Frames,
    [property: JsonPropertyName("mime_type")]
    string MimeType,
    [property: JsonPropertyName("originalSrc")]
    string OriginalSource,
    [property: JsonPropertyName("src")]
    string Source
);

public sealed record UgoiraFrame(
    [property: JsonPropertyName("file")]
    string File,
    [property: JsonPropertyName("delay")]
    int Delay
);

public sealed record UserDetailsResponse(
    [property: JsonPropertyName("body")]
    UserDetails User
);

public sealed record UserDetails(
    [property: JsonPropertyName("userId")]
    string UserId,
    [property: JsonPropertyName("image")]
    string AvatarUrl,
    [property: JsonPropertyName("imageBig")]
    string LargeAvatarUrl
);

#endregion
