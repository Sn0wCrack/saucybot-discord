using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Library;
using SaucyBot.Library.Sites.DeviantArt;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed partial class DeviantArt : BaseSite, IDeviantArtSite
{
    public override string Identifier => "DeviantArt";

    [GeneratedRegex(@"https?://(www\.)?deviantart\.com/(?<author>\S+)/art/(?<slug>\S+)/?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DeviantArtPattern();

    protected override Regex Pattern => DeviantArtPattern();

    public override Color Color => new(0x00E59B);

    private readonly ILogger<DeviantArt> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDeviantArtClient _client;
    private readonly IDeviantArtOpenEmbedClient _openEmbedClient;

    public DeviantArt(ILogger<DeviantArt> logger, IConfiguration configuration, IDeviantArtClient client, IDeviantArtOpenEmbedClient openEmbedClient)
    {
        _logger = logger;
        _configuration = configuration;
        _client = client;
        _openEmbedClient = openEmbedClient;
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        var response = new ProcessResponse();

        var url = request.Match.Value;

        var openEmbed = await _openEmbedClient.Get(url);

        if (openEmbed is null)
        {
            return null;
        }

        var embed = new EmbedBuilder
        {
            Title = openEmbed.Title,
            Url = openEmbed.Url,
            Color = this.Color,
            ImageUrl = openEmbed.Url,
            Author = new EmbedAuthorBuilder
            {
                Name = openEmbed.AuthorName,
                Url = openEmbed.AuthorUrl
            },
            Footer = new EmbedFooterBuilder
            {
                IconUrl = Constants.DeviantArtIconUrl,
                Text = "DeviantArt"
            }
        };

        response.Embeds.Add(embed.Build());

        return response;
    }
}
