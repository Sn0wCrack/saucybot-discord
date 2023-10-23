using System.Net;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed class Reddit : BaseSite
{
    public override string Identifier => "Reddit";

    protected override string Pattern => @"https?:\/\/(www\.)?reddit\.com\/media\?url=(?<url>[A-Z0-9\%\.]+)";

    protected override Color Color => new(0xFF4500);

    private readonly ILogger<Reddit> _logger;
    
    public Reddit(ILogger<Reddit> logger)
    {
        _logger = logger;
    }

    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        // TODO: Handle v.redd.it links using youtube-dl or similar
        
        var response = new ProcessResponse();
        
        var originalUrl = WebUtility.UrlDecode(match.Groups["url"].Value);
        
        response.Text = originalUrl;

        return response;
    }
}
