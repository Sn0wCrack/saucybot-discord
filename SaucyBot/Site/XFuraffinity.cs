using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed partial class XFuraffinity : BaseSite
{
    public override string Identifier => "XFuraffinity";

    [GeneratedRegex(@"https?://(?:(?!xfuraffinity\.net)(?:www\.)?)?furaffinity\.net/(?<path>(?:view|full)/(?<id>\d+))/?(?<query>\?[^\s#]*)?(?<fragment>\#[^\s]*)?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex XFuraffinityPattern();

    protected override Regex Pattern => XFuraffinityPattern();

    protected override Color Color => new(0x8B5CF6);

    private readonly ILogger<XFuraffinity> _logger;

    public XFuraffinity(ILogger<XFuraffinity> logger)
    {
        _logger = logger;
    }

    public override Task<ProcessResponse?> Process(ProcessRequest request)
    {
        var originalUrl = request.Match.Value;
        var path = request.Match.Groups["path"].Value;
        var query = request.Match.Groups["query"].Success ? request.Match.Groups["query"].Value : string.Empty;
        var fragment = request.Match.Groups["fragment"].Success ? request.Match.Groups["fragment"].Value : string.Empty;

        var rewrittenUrl = $"https://xfuraffinity.net/{path}{query}{fragment}";

        _logger.LogDebug("Rewrote FurAffinity URL: {Original} -> {Rewritten}", originalUrl, rewrittenUrl);

        return Task.FromResult<ProcessResponse?>(new ProcessResponse { Text = rewrittenUrl });
    }
}
