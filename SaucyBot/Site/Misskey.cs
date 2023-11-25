using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Library.Sites.Misskey;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed class Misskey : BaseSite
{
    public override string Identifier => "Misskey";

    protected override string Pattern => @"https?:\/\/(www\.)?misskey\.io\/notes\/(?<id>[0-9a-z]+)";

    protected override Color Color => new Color(0x85B300);

    private readonly ILogger<Misskey> _logger;
    private readonly IMisskeyClient _client;

    public Misskey(
        ILogger<Misskey> logger,
        IMisskeyClient client
    ) {
        _logger = logger;
        _client = client;
    }

    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        var note = await _client.ShowNote(match.Groups["id"].Value);

        if (note is null)
        {
            return null;
        }

        var response = new ProcessResponse();

        var embed = new EmbedBuilder
        {
            Color = this.Color,
            Description = note.Text ?? "",
            Author = new EmbedAuthorBuilder
            {
                Name = $"{note.User.Name} ({note.User.Username})",
                IconUrl = note.User.AvatarUrl
            }
        };
        
        response.Embeds.Add(embed.Build());

        return response;
    }
}
