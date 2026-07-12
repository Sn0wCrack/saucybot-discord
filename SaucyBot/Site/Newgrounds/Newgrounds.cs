using System.Text.RegularExpressions;
using Discord;
using SaucyBot.Common;
using SaucyBot.Library.Sites.Newgrounds;
using SaucyBot.Site.Response;

namespace SaucyBot.Site.Newgrounds;


public sealed partial class NewgroundsSite : BaseSite, INewgroundsSite
{
    public override string Identifier => "Newgrounds";

    [GeneratedRegex(@"https?://(www\.)?newgrounds\.com/art/view/(?<user>\S*)/(?<slug>\S+)/?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex NewgroundsPattern();

    public override Regex Pattern => NewgroundsPattern();

    public override Color Color => new(0xFFF17A);

    private readonly ILogger<NewgroundsSite> _logger;
    private readonly INewgroundsClient _client;

    public NewgroundsSite(ILogger<NewgroundsSite> logger, INewgroundsClient client)
    {
        _logger = logger;
        _client = client;
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        var response = new ProcessResponse();

        var post = await _client.GetArt(request.Match.Groups["user"].Value, request.Match.Groups["slug"].Value);

        if (post is null)
        {
            return null;
        }

        var embed = new EmbedBuilder
        {
            Title = post.Title(),
            Description = Helper.ProcessDescription(post.Description() ?? ""),
            Url = request.Match.Value,
            Color = this.Color,
            ImageUrl = post.ImageUrl(),
            Fields = new List<EmbedFieldBuilder>
            {
                new()
                {
                    Name = "Views",
                    Value = post.Views(),
                    IsInline = true,
                },
                new()
                {
                    Name = "Score",
                    Value = $"{post.Score()} / 5.00",
                    IsInline = true,
                }
            },
            Footer = new EmbedFooterBuilder { Text = "Newgrounds" },
        };

        response.Embeds.Add(embed.Build());

        return response;
    }
}
