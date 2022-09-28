﻿using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Common;
using SaucyBot.Library;
using SaucyBot.Library.Sites.ExHentai;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public sealed class ExHentai : BaseSite
{
    public override string Identifier => "ExHentai";

    protected override Color Color => new(0x660611);

    protected override string Pattern => @"https?:\/\/(www\.)?e[x-]hentai\.org\/g\/(?<id>\d+)\/(?<hash>\S+)\/?";

    private readonly ILogger<ExHentai> _logger;
    private readonly ExHentaiClient _client;

    public ExHentai(ILogger<ExHentai> logger, ExHentaiClient client)
    {
        _logger = logger;
        _client = client;
    }
    
    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        var response = new ProcessResponse();

        var url = match.Value;

        var request = new ExHentaiGalleryRequest(
            url.ToLowerInvariant().Contains("exhentai") ? ExHentaiRequestMode.ExHentai : ExHentaiRequestMode.EHentai,
            match.Groups["id"].Value,
            match.Groups["hash"].Value
        );

        var page = await _client.GetGallery(request);

        if (page is null)
        {
            return response;
        }

        var embed = new EmbedBuilder
        {
            Title = page.Title(),
            Description = await Helper.ProcessDescription(page.Description() ?? ""),
            Url = url,
            Color = this.Color,
            ImageUrl = page.ImageUrl(),
            Timestamp = page.PostedAt(),
            Author = new EmbedAuthorBuilder
            {
                Name = page.AuthorName(),
                Url = page.AuthorUrl(),
            },
            Fields = new List<EmbedFieldBuilder>
            {
                new()
                {
                    Name = "Language",
                    Value = page.Language() ?? "N/A",
                    IsInline = true,
                },
                new()
                {
                    Name = "Pages",
                    Value = page.Length() ?? "N/A",
                    IsInline = true,
                },
                new ()
                {
                    Name = "Rating",
                    Value = $"{page.Rating()} / 5.00",
                    IsInline = true,
                }
            },
            Footer = new EmbedFooterBuilder
            {
                IconUrl = Constants.EHentaiIconUrl,
                Text = url.ToLowerInvariant().Contains("exhentai") ? "exhentai" : "e-hentai",
            }
        };
        
        response.Embeds.Add(embed.Build());

        return response;
    }
}
