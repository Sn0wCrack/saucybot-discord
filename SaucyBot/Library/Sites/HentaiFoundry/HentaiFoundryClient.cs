using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.HentaiFoundry;

public sealed class HentaiFoundryClient : IHentaiFoundryClient
{
    private const string BaseUrl = "https://www.hentai-foundry.com";

    private readonly ILogger<HentaiFoundryClient> _logger;
    private readonly IConfiguration _configuration;
    private readonly ICacheManager _cache;

    private readonly HttpClient _client;

    public HentaiFoundryClient(
        ILogger<HentaiFoundryClient> logger,
        IConfiguration configuration,
        ICacheManager cacheManager,
        HttpClient client
    )
    {
        _logger = logger;
        _configuration = configuration;
        _cache = cacheManager;
        _client = client;
    }

    public async Task<bool> Agree()
    {
        await _client.GetAsync($"{BaseUrl}/?enterAgree=1");

        return true;
    }

    public async Task<HentaiFoundryPicture?> GetPage(string url)
    {
        return await _cache.Remember(
            $"hentaifoundry.picture_{url}",
            async () => new HentaiFoundryPicture(await _client.GetStringAsync(url))
        );
    }
}

public sealed class HentaiFoundryPicture
{
    private const string BaseUrl = "https://www.hentai-foundry.com";

    public string? TitleValue { get; init; }
    public string? DescriptionValue { get; init; }
    public string? ImageSourceValue { get; init; }
    public string? AuthorNameValue { get; init; }
    public string? AuthorSourceValue { get; init; }
    public string? AuthorAvatarSourceValue { get; init; }
    public DateTimeOffset? PostedAtValue { get; init; }
    public string ViewsValue { get; init; } = "0";
    public string VotesValue { get; init; } = "0";

    public HentaiFoundryPicture()
    {
    }

    public HentaiFoundryPicture(string page)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument(page);

        TitleValue = document.QuerySelector(".imageTitle")?.TextContent;
        DescriptionValue = document.QuerySelector(".picDescript")?.TextContent;

        ImageSourceValue = document.QuerySelector("#picBox .boxbody img")?.GetAttribute("src");

        AuthorNameValue = document.QuerySelector("#descriptionBox .boxbody a img")?.GetAttribute("title");
        AuthorSourceValue = document.QuerySelector("#descriptionBox .boxbody a")?.GetAttribute("href");
        AuthorAvatarSourceValue = document.QuerySelector("#descriptionBox .boxbody a img")?.GetAttribute("src");

        var dateTime = document.QuerySelector("#pictureGeneralInfoBox time")?.GetAttribute("datetime");
        PostedAtValue = dateTime is null ? null : DateTimeOffset.Parse(dateTime);

        ViewsValue = document.QuerySelector("#pictureGeneralInfoBox .boxbody .column span:contains('Views')")
            ?.NextSibling?.TextContent.Trim() ?? "0";
        VotesValue = document.QuerySelector("#pictureGeneralInfoBox .boxbody .column span:contains('Vote Score')")
            ?.NextSibling?.TextContent.Trim() ?? "0";
    }

    public string? Title() => TitleValue;
    public string? Description() => DescriptionValue;
    public string? ImageSrc() => ImageSourceValue;
    public string? ImageUrl() => ImageSrc() is null ? null : $"https:{ImageSrc()}";
    public string? AuthorName() => AuthorNameValue;
    public string? AuthorSrc() => AuthorSourceValue;
    public string? AuthorUrl() => AuthorSrc() is null ? null : $"{BaseUrl}{AuthorSrc()}";
    public string? AuthorAvatarSrc() => AuthorAvatarSourceValue;
    public string? AuthorAvatarUrl() => AuthorAvatarSrc() is null ? null : $"https:{AuthorAvatarSrc()}";
    public DateTimeOffset? PostedAt() => PostedAtValue;
    public string Views() => ViewsValue;
    public string Votes() => VotesValue;
}
