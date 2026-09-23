using System.Text.RegularExpressions;
using Discord;
using SaucyBot.Common;
using SaucyBot.Library.Sites.FurAffinity;

namespace SaucyBot.Site.FurAffinity;

[SiteIdentifier("FurAffinity")]
public sealed partial class FaExportFurAffinitySite : BaseSite
{
    [GeneratedRegex(@"https?://(www\.)?furaffinity\.net/(?:view|full)/(?<id>\d+)/?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex FurAffinityPattern();

    public override Regex Pattern => FurAffinityPattern();

    private readonly ILogger<FaExportFurAffinitySite> _logger;
    private readonly IFaExportClient _client;

    public FaExportFurAffinitySite(ILogger<FaExportFurAffinitySite> logger, IFaExportClient client)
    {
        _logger = logger;
        _client = client;
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        var submission = await _client.GetSubmission(
            request.Match.Groups["id"].Value
        );

        if (submission is null)
        {
            return null;
        }

        var response = new ProcessResponse
        {
            IsNsfw = submission.IsNsfw,
        };

        var embed = new EmbedBuilder
        {
            Title = submission.Title,
            Description = Helper.ProcessDescription(FurAffinityBbCodeCleaner.Clean(submission.Description)),
            Color = Color,
            Url = submission.Link,
            ImageUrl = submission.Download,
            Timestamp = DateTimeOffset.Parse(submission.PostedAt),
            Author = new EmbedAuthorBuilder
            {
                Name = submission.ProfileName,
                Url = submission.Profile,
                IconUrl = submission.Avatar,
            },
            Fields = new List<EmbedFieldBuilder>
            {
                new()
                {
                    Name = "Views",
                    Value = submission.Views,
                    IsInline = true,
                },
                new()
                {
                    Name = "Favorties",
                    Value = submission.Favorites,
                    IsInline = true,
                },
                new()
                {
                    Name = "Comments",
                    Value = submission.Comments,
                    IsInline = true,
                }
            },
            Footer = new EmbedFooterBuilder { Text = "FurAffinity" },
        };

        response.Embeds.Add(embed.Build());

        return response;
    }
}
