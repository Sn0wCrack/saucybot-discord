using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Common;
using SaucyBot.Library.Sites.Newgrounds;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed class Newgrounds : BaseSite
{
    public override string Identifier => "Newgrounds";

    protected override Color Color => new(0xFFF17A);
    protected override string Pattern => @"https?:\/\/(www\.)?newgrounds\.com\/art\/view\/(?<user>.*)\/(?<slug>\S+)\/?";

    private readonly ILogger<Newgrounds> _logger;
    private readonly INewgroundsClient _client;

    public Newgrounds(ILogger<Newgrounds> logger, INewgroundsClient client)
    {
        _logger = logger;
        _client = client;
    }
    
    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        var response = new ProcessResponse();

        var post = await _client.GetArt(match.Groups["user"].Value, match.Groups["slug"].Value);

        if (post is null)
        {
            return null;
        }

        var embed = new EmbedBuilder
        {
            Title = post.Title(),
            Description = await Helper.ProcessDescription(post.Description() ?? ""),
            Url = match.Value,
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
