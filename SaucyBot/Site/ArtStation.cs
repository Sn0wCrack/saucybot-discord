using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Common;
using SaucyBot.Library;
using SaucyBot.Library.Sites.ArtStation;
using SaucyBot.Services;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public class ArtStation : BaseSite
{
    public override string Identifier => "ArtStation";

    protected override string Pattern => @"https?:\/\/(www\.)?artstation\.com\/artwork\/(?<hash>\S+)";

    private readonly ILogger<ArtStation> _logger;
    private readonly IConfiguration _configuration;
    private readonly ArtStationClient _client;

    public ArtStation(ILogger<ArtStation> logger, IConfiguration configuration, ArtStationClient client)
    {
        _logger = logger;
        _configuration = configuration;
        _client = client;
    }
    
    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        var response = new ProcessResponse();

        var project = await _client.GetProject(match.Groups["hash"].Value);

        if (project is null)
        {
            return null;
        }

        var limit = _configuration.GetSection("Sites:ArtStation:PostLimit").Get<int>();

        // Discord embeds the first ArtStation item, so if there's only one, ignore the request
        if (project.Assets.Count == 1)
        {
            return null;
        }

        if (project.Assets.Count > limit)
        {
            response.Text = $"This is part of a {project.Assets.Count} image set.";
        }

        var parsed = new Uri(project.CoverUrl);

        var coverFileName = Path.GetFileName(parsed.AbsolutePath);
        
        project.Assets.RemoveAt(0);

        var assets = project.Assets
            .Where(asset => asset.Type is "image" or "cover" && !asset.ImageUrl.Contains(coverFileName))
            .ToList()
            .GetRange(1, Math.Min(project.Assets.Count - 1, limit));
            

        foreach (var asset in assets)
        {
            var description = await Helper.ProcessDescription(project.Description);

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
            };
            
            response.Embeds.Add(embed.Build());
        }

        return response;
    }
}
