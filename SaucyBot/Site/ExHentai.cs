using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Common;
using SaucyBot.Library.Sites.ExHentai;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public class ExHentai : BaseSite
{
    public override string Identifier => "ExHentai";

    protected override Color Color => new(0x660611);

    protected override string Pattern => @"https?:\/\/(www\.)?e[x-]hentai\.org\/g\/(?<id>\d+)\/(?<hash>\S+)\/?";

    private readonly ILogger<ExHentai> _logger;
    private readonly ExHentaiClient _client;

    public ExHentai(ILogger<ExHentai> logger, ExHentaiClient client)
    {
        _logger = logger;
        _client = client;
    }
    
    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        var response = new ProcessResponse();

        var page = await _client.GetGallery(match.Value);

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
        };
        
        response.Embeds.Add(embed.Build());

        return response;
    }
}
