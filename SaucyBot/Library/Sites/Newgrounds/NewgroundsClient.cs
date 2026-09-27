using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using SaucyBot.Services;

namespace SaucyBot.Library.Sites.Newgrounds;

public sealed class NewgroundsClient : INewgroundsClient
{
    private const string BaseUrl = "https://www.newgrounds.com";

    private readonly ILogger<NewgroundsClient> _logger;
    private readonly IConfiguration _configuration;
    private readonly ICacheManager _cache;

    private readonly HttpClient _client;

    public NewgroundsClient(
        ILogger<NewgroundsClient> logger,
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

    public async Task<NewgroundsArt?> GetArt(string user, string slug)
    {
        var url = $"{BaseUrl}/art/view/{user}/{slug}";

        return await _cache.Remember(
            $"newgrounds.art_{user}_{slug}",
            async () => new NewgroundsArt(await _client.GetStringAsync(url))
        );
    }
}

public sealed class NewgroundsArt
{
    public string? TitleValue { get; init; }
    public string? DescriptionValue { get; init; }
    public string? ImageUrlValue { get; init; }
    public string ViewsValue { get; init; } = "0";
    public string ScoreValue { get; init; } = "0.00";

    public NewgroundsArt()
    {
    }

    public NewgroundsArt(string page)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument(page);

        TitleValue = document.QuerySelector(".body-guts .column.wide.right .pod-head h2")?.TextContent;
        DescriptionValue = document.QuerySelector("#author_comments")?.InnerHtml;
        ImageUrlValue = document.QuerySelector(".pod-body .image img")?.GetAttribute("src");
        ViewsValue = document.QuerySelector(".sidestats dt:contains('Views')")?.NextElementSibling?.TextContent ?? "0";
        ScoreValue = document.QuerySelector("#score_number")?.TextContent ?? "0.00";
    }

    public string? Title() => TitleValue;
    public string? Description() => DescriptionValue;
    public string? ImageUrl() => ImageUrlValue;
    public string Views() => ViewsValue;
    public string Score() => ScoreValue;
}
