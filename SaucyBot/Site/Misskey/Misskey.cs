using System.Text.RegularExpressions;
using Discord;
using SaucyBot.Library;
using SaucyBot.Library.Sites.Misskey;

namespace SaucyBot.Site.Misskey;


public sealed class MisskeySite : BaseSite, IMisskeySite
{
    public override string Identifier => "Misskey";

    public override Color Color => new(0x85B300);

    private readonly ILogger<MisskeySite> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMisskeyClient _client;

    public MisskeySite(
        ILogger<MisskeySite> logger,
        IConfiguration configuration,
        IMisskeyClient client
    )
    {
        _logger = logger;
        _configuration = configuration;
        _client = client;

        var domains = new List<string> { "misskey.io", "misskey.design", "oekakiskey.com" }
            .Select(Regex.Escape);

        var regex = String.Join("|", domains);

        Pattern = new Regex(@$"(?<url>https?://(www\.)?({regex}))/notes/(?<id>[0-9a-z]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        var url = request.Match.Groups["url"].Value;

        var id = request.Match.Groups["id"].Value;

        var note = await _client.ShowNote(url, id);

        if (note is null)
        {
            return null;
        }

        var hasEmbed = false;

        // If we have a request.Message attached, we need to wait a bit for Discord to process the embed,
        // we when need to refresh the request.Message and see if an embed has been added in that time.
        if (request.Context?.Message is { } message)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_configuration.GetSection("Sites:Misskey:Delay").Get<double>()),
                request.Context.CancellationToken);

            // NOTE: Discord.NET works a little interestingly, basically when a request.Message updates the Bot learns of this change
            // and then proceeds to update its internal cache, so while we're waiting around it should update the request.Message cache
            // automatically, so there's no need to refresh the request.Message object.

            hasEmbed = (await message.GetLatestEmbedsAsync(request.Context.CancellationToken)).Count != 0;
        }

        if (hasEmbed && !ShouldEmbed(note))
        {
            return null;
        }

        var response = new ProcessResponse();

        foreach (var file in note.Files)
        {
            if (!file.Type.StartsWith("image/"))
            {
                continue;
            }

            if (file.IsSensitive)
            {
                response.IsNsfw = true;
            }

            var embed = new EmbedBuilder
            {
                Url = request.Match.Value,
                Timestamp = DateTimeOffset.Parse(note.CreatedAt),
                Color = this.Color,
                Description = note.Text ?? "",
                Author = new EmbedAuthorBuilder
                {
                    Name = $"{note.User.Name} ({note.User.Username})",
                    IconUrl = note.User.AvatarUrl,
                    Url = $"{url}/@{note.User.Username}"
                },
                ImageUrl = file.Url,
                Footer = new EmbedFooterBuilder { IconUrl = Constants.MisskeyIconUrl, Text = "Misskey" },
            };

            response.Embeds.Add(embed.Build());
        }

        return response;
    }

    private static bool ShouldEmbed(ShowNoteResponse note)
    {
        return note.Files.Count > 1 || note.Files.Any(file => file.IsSensitive);
    }
}
