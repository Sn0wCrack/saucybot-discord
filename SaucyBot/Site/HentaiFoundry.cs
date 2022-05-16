using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Common;
using SaucyBot.Library;
using SaucyBot.Library.Sites.HentaiFoundry;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public class HentaiFoundry : BaseSite
{
    public override string Identifier => "HentaiFoundry";

    protected override string Pattern =>
        @"https:?\/\/(www\.)?hentai-foundry\.com\/pictures\/user\/(?<user>.*)\/(?<id>\d+)\/(?<slug>\S+)";

    protected override Color Color => new(0xFF67A2);

    private readonly ILogger<HentaiFoundry> _logger;
    private readonly HentaiFoundryClient _client;

    public HentaiFoundry(ILogger<HentaiFoundry> logger, HentaiFoundryClient client)
    {
        _logger = logger;
        _client = client;
    }

    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        var response = new ProcessResponse();

        if (!await _client.Agree())
        {
            _logger.LogError("HentaiFoundry over 18 agreement failed, cookie was not present");
            return null;
        }

        var page = await _client.GetPage(match.Value);

        if (page is null)
        {
            return response;
        }

        var embed = new EmbedBuilder
        {
            Title = page.Title(),
            Description = await Helper.ProcessDescription(page.Description() ?? ""),
            Url = match.Value,
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
