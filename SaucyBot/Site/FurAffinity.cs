﻿using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Library.Sites.FurAffinity;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public class FurAffinity : BaseSite
{
    public override string Identifier => "FurAffinity";
    protected override string Pattern => @"https?:\/\/(www\.)?furaffinity\.net\/(?:view|full)\/(?<id>\d+)";

    private readonly ILogger<FurAffinity> _logger;
    private readonly FaExportClient _client;

    public FurAffinity(ILogger<FurAffinity> logger, FaExportClient client)
    {
        _logger = logger;
        _client = client;
    }
    
    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        var response = new ProcessResponse();

        var submission = await _client.GetSubmission(
            match.Groups["id"].Value
        );
        
        if (submission is null)
        {
            return null;
        }

        var embed = new EmbedBuilder()
        {
            Title = submission.Title,
            Description = submission.Description,
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
        };
        
        response.Embeds.Add(embed.Build());
            
        return response;
    }
}
