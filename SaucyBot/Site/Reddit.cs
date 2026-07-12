using System.Net;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed partial class Reddit : BaseSite
{
    public override string Identifier => "Reddit";

    [GeneratedRegex(@"https?://(www\.)?reddit\.com/media\?url=(?<url>[A-Z0-9\%\.]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex RedditPattern();

    protected override Regex Pattern => RedditPattern();

    protected override Color Color => new(0xFF4500);

    private readonly ILogger<Reddit> _logger;

    public Reddit(ILogger<Reddit> logger)
    {
        _logger = logger;
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        // TODO: Handle v.redd.it links using youtube-dl or similar

        var response = new ProcessResponse();

        var originalUrl = WebUtility.UrlDecode(request.Match.Groups["url"].Value);

        response.Text = originalUrl;

        return response;
    }
}
