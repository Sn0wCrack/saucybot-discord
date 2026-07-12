using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Common;
using SaucyBot.Extensions;
using SaucyBot.Library;
using SaucyBot.Library.Sites.ArtStation;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed partial class ArtStation : BaseSite
{
    public override string Identifier => "ArtStation";

    [GeneratedRegex(@"https?://(www\.)?artstation\.com/artwork/(?<hash>\S+)/?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ArtStationPattern();

    protected override Regex Pattern => ArtStationPattern();

    private readonly ILogger<ArtStation> _logger;
    private readonly IConfiguration _configuration;
    private readonly IArtStationClient _client;

    public ArtStation(ILogger<ArtStation> logger, IConfiguration configuration, IArtStationClient client)
    {
        _logger = logger;
        _configuration = configuration;
        _client = client;
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        var response = new ProcessResponse();

        var project = await _client.GetProject(request.Match.Groups["hash"].Value);

        if (project is null)
        {
            return null;
        }

        var limit = _configuration.GetSection("Sites:ArtStation:PostLimit").Get<int>();

        if (project.Assets.Count > limit)
        {
            response.Text = $"This is part of a {project.Assets.Count} image set.";
        }

        var assets = project.Assets
            .Where(asset => asset.Type is "image" or "cover")
            .ToList()
            .SafeSlice(0, limit);

        var description = Helper.ProcessDescription(project.Description);

        foreach (var asset in assets)
        {

            var embed = new EmbedBuilder
            {
                Title = string.IsNullOrEmpty(asset.Title) ? project.Title : asset.Title,
                Description = description,
                Color = Color,
                Url = project.Permalink,
                ImageUrl = asset.ImageUrl,
                Timestamp = DateTimeOffset.Parse(project.PublishedAt),
                Author = new EmbedAuthorBuilder
                {
                    Name = project.User.FullName,
                    Url = project.User.Permalink,
                    IconUrl = project.User.MediumAvatarUrl,
                },
                Fields = new List<EmbedFieldBuilder>
                {
                    new()
                    {
                        Name = "Views",
                        Value = project.ViewsCount,
                        IsInline = true,
                    },
                    new()
                    {
                        Name = "Likes",
                        Value = project.LikesCount,
                        IsInline = true,
                    },
                },
                Footer = new EmbedFooterBuilder { IconUrl = Constants.ArtStationIconUrl, Text = "ArtStation" },
            };

            response.Embeds.Add(embed.Build());
        }

        return response;
    }
}
