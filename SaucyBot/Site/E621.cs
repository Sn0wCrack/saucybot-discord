using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Extensions;
using SaucyBot.Library.Sites.E621;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed class E621 : BaseSite
{
    public override string Identifier => "E621";

    protected override string Pattern => @"https?:\/\/(www\.)?e621\.net\/posts\/(?<id>\d+)\/?";

    protected override Color Color => new (0x00549E);
    
    private readonly ILogger<E621> _logger;
    private readonly E621Client _client;

    public E621(ILogger<E621> logger, E621Client client)
    {
        _logger = logger;
        _client = client;
    }

    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        var response = new ProcessResponse();
        
        var url = match.Value;

        var post = await _client.GetPost(match.Groups["id"].Value);

        if (post is null)
        {
            return null;
        }

        var prefix = post.Post.Tags.Meta.Contains("animated")
            ? "[ANIM]"
            : "";

        var imageUrl = post.Post.File.Url;

        if (post.Post.File.Extension.IsIn("webm", "swf"))
        {
            imageUrl = post.Post.Sample.Has
                ? post.Post.Sample.Url
                : post.Post.Preview.Url;
        }

        var fields = new List<EmbedFieldBuilder>();

        if (post.Post.Tags.Artist.Length >= 1)
        {
            var artists = post.Post.Tags.Artist.Select(artist => artist.ToTitleCase());
            
            var value = string.Join(", ", artists);
            
            fields.Add(new EmbedFieldBuilder
            {
                Name = "Artist",
                Value = value,
                IsInline = true
            });
        }
        
        fields.Add(new EmbedFieldBuilder
        {
            Name = "Score",
            Value = post.Post.Score.Total.ToString(),
            IsInline = true
        });

        var embed = new EmbedBuilder
        {
            Title = $"{prefix} Post #{match.Groups["id"].Value}".Trim(),
            Url = url,
            Color = this.Color,
            Timestamp = DateTimeOffset.Parse(post.Post.CreatedAt),
            Description = post.Post.Description,
            ImageUrl = imageUrl,
            Fields = fields,
            Footer = new EmbedFooterBuilder { Text = "e621" }
        };
        
        response.Embeds.Add(embed.Build());

        return response;
    }
}
