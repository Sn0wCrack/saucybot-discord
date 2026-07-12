using System.Text.RegularExpressions;
using Discord;
using SaucyBot.Common;
using SaucyBot.Library;
using SaucyBot.Library.Sites.ExHentai;
using SaucyBot.Site.Response;

namespace SaucyBot.Site.ExHentai;


public sealed partial class ExHentaiSite : BaseSite, IExHentaiSite
{
    public override string Identifier => "ExHentai";

    [GeneratedRegex(@"https?://(www\.)?e[x-]hentai\.org/g/(?<id>\d+)/(?<hash>\S+)/?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ExHentaiPattern();

    public override Regex Pattern => ExHentaiPattern();

    public override Color Color => new(0x660611);

    private readonly ILogger<ExHentaiSite> _logger;
    private readonly IConfiguration _configuration;
    private readonly IExHentaiClient _client;
    private readonly bool _isConfiguredToEmbedExHentaiLinks;

    public ExHentaiSite(ILogger<ExHentaiSite> logger, IConfiguration configuration, IExHentaiClient client)
    {
        _logger = logger;
        _configuration = configuration;
        _client = client;

        _isConfiguredToEmbedExHentaiLinks = IsConfiguredToEmbedExHentaiLinks();
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        var response = new ProcessResponse();

        var url = request.Match.Value;

        var isExHentaiLink = url.Contains("exhentai", StringComparison.InvariantCultureIgnoreCase);

        if (
            isExHentaiLink &&
            !_isConfiguredToEmbedExHentaiLinks
        )
        {
            return null;
        }

        var galleryRequest = new ExHentaiGalleryRequest(
            isExHentaiLink ? ExHentaiRequestMode.ExHentai : ExHentaiRequestMode.EHentai,
            request.Match.Groups["id"].Value,
            request.Match.Groups["hash"].Value
        );

        var page = await _client.GetGallery(galleryRequest);

        if (page is null)
        {
            return null;
        }

        var embed = new EmbedBuilder
        {
            Title = page.Title(),
            Description = Helper.ProcessDescription(page.Description() ?? ""),
            Url = url,
            Color = this.Color,
            ImageUrl = page.ImageUrl(),
            Timestamp = page.PostedAt(),
            Author = new EmbedAuthorBuilder
            {
                Name = page.AuthorName(),
                Url = page.AuthorUrl(),
            },
            Fields = new List<EmbedFieldBuilder>
            {
                new()
                {
                    Name = "Language",
                    Value = page.Language() ?? "N/A",
                    IsInline = true,
                },
                new()
                {
                    Name = "Pages",
                    Value = page.Length() ?? "N/A",
                    IsInline = true,
                },
                new ()
                {
                    Name = "Rating",
                    Value = $"{page.Rating()} / 5.00",
                    IsInline = true,
                }
            },
            Footer = new EmbedFooterBuilder
            {
                IconUrl = Constants.EHentaiIconUrl,
                Text = isExHentaiLink ? "exhentai" : "e-hentai",
            }
        };

        response.Embeds.Add(embed.Build());

        return response;
    }

    private bool IsConfiguredToEmbedExHentaiLinks()
    {
        var memberId = _configuration.GetSection("Sites:ExHentai:Cookies:MemberId").Get<string?>();
        var passwordHash = _configuration.GetSection("Sites:ExHentai:Cookies:PasswordHash").Get<string?>();

        return memberId is not (null or "") && passwordHash is not (null or "");
    }
}
