using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Library;
using SaucyBot.Library.Sites.DeviantArt;
using SaucyBot.Site.Response;


namespace SaucyBot.Site;

public sealed class DeviantArt : BaseSite
{
    public override string Identifier => "DeviantArt";

    protected override string Pattern => @"https?:\/\/(www\.)?deviantart\.com\/(?<author>\S+)\/art\/(?<slug>\S+)\/?";

    protected override Color Color => new(0x00E59B);

    private readonly ILogger<DeviantArt> _logger;
    private readonly IConfiguration _configuration;
    private readonly DeviantArtClient _client;
    private readonly DeviantArtOpenEmbedClient _openEmbedClient;
    
    public DeviantArt(ILogger<DeviantArt> logger, IConfiguration configuration, DeviantArtClient client, DeviantArtOpenEmbedClient openEmbedClient)
    {
        _logger = logger;
        _configuration = configuration;
        _client = client;
        _openEmbedClient = openEmbedClient;
    }
    
    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        var response = new ProcessResponse();

        var url = match.Value;

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
