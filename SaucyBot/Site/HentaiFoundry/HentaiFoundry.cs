using System.Text.RegularExpressions;
using Discord;
using SaucyBot.Common;
using SaucyBot.Library;
using SaucyBot.Library.Sites.HentaiFoundry;
using SaucyBot.Site.Response;

namespace SaucyBot.Site.HentaiFoundry;

using SaucyBot.Site;

public sealed partial class HentaiFoundrySite : BaseSite, IHentaiFoundrySite
{
    public override string Identifier => "HentaiFoundry";

    [GeneratedRegex(@"https?://(www\.)?hentai-foundry\.com/pictures/user/(?<user>\S*)/(?<id>\d+)/(?<slug>\S+)/?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex HentaiFoundryPattern();

    public override Regex Pattern => HentaiFoundryPattern();

    public override Color Color => new(0xFF67A2);

    private readonly ILogger<HentaiFoundrySite> _logger;
    private readonly IHentaiFoundryClient _client;

    public HentaiFoundrySite(ILogger<HentaiFoundrySite> logger, IHentaiFoundryClient client)
    {
        _logger = logger;
        _client = client;
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        var response = new ProcessResponse();

        if (!await _client.Agree())
        {
            _logger.LogError("HentaiFoundry over 18 agreement failed, cookie was not present");
            return null;
        }

        var page = await _client.GetPage(request.Match.Value);

        if (page is null)
        {
            return null;
        }

        var embed = new EmbedBuilder
        {
            Title = page.Title(),
            Description = Helper.ProcessDescription(page.Description() ?? ""),
            Url = request.Match.Value,
            Color = this.Color,
            ImageUrl = page.ImageUrl(),
            Timestamp = page.PostedAt(),
            Author = new EmbedAuthorBuilder
            {
                Name = page.AuthorName(),
                Url = page.AuthorUrl(),
                IconUrl = page.AuthorAvatarUrl(),
            },
            Fields = new List<EmbedFieldBuilder>
            {
                new()
                {
                    Name = "Views",
                    Value = page.Views(),
                    IsInline = true,
                },
                new()
                {
                    Name = "Votes",
                    Value = page.Votes(),
                    IsInline = true,
                }
            },
            Footer = new EmbedFooterBuilder { IconUrl = Constants.HentaiFoundryIconUrl, Text = "HentaiFoundry" },
        };

        response.Embeds.Add(embed.Build());

        return response;
    }
}
