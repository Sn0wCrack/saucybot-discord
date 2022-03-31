using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Library;
using SaucyBot.Library.Sites.Pixiv;
using SaucyBot.Site.Response;

namespace SaucyBot.Site;

public class Pixiv : BaseSite
{
    public override string Identifier => "Pixiv";
    protected override string Pattern => @"https?:\/\/(www\.)?pixiv\.net\/.*artworks\/(?<id>\d+)";

    private readonly PixivClient _client;
    private readonly ILogger<Pixiv> _logger;
    private readonly IConfiguration _configuration;
    
    public Pixiv(ILogger<Pixiv> logger, IConfiguration configuration, PixivClient client)
    {
        _logger = logger;
        _configuration = configuration;
        _client = client;
    }

    public override async Task<ProcessResponse?> Process(Match match, SocketUserMessage? message = null)
    {
        if (!await _client.Login())
        {
            _logger.LogError("Pixiv login check failed, cookie may be expired or invalid.");
            return null;
        }

        var id = match.Groups["id"].Value;

        var response = await _client.IllustrationDetails(id);

        if (response is null)
        {
            return null;
        }

        if (response.IllustrationDetails.Type == IllustrationType.Ugoira)
        {
            return await ProcessUgoira(response);
        }

        return await ProcessImage(response);
    }

    private async Task<ProcessResponse?> ProcessUgoira(IllustrationDetailsResponse? illustrationDetails)
    {
        var response = new ProcessResponse();

        return response;
    }

    private async Task<ProcessResponse?> ProcessImage(IllustrationDetailsResponse illustrationDetails)
    {
        var message = new ProcessResponse();

        var pageCount = illustrationDetails.IllustrationDetails.PageCount;

        if (pageCount == 1)
        {
            var url = await DetermineHighestUsableQualityFile(
                illustrationDetails.IllustrationDetails.IllustrationDetailsUrls.All
            );

            if (url is null)
            {
                return message;
            }

            var file = await GetFile(url);
            
            message.Files.Add(file);

            return message;
        }

        var response = await _client.IllustrationPages(illustrationDetails.IllustrationDetails.Id);

        if (response is null)
        {
            return message;
        }

        var postLimit = _configuration.GetSection("Sites:Pixiv:PostLimit").Get<int>();

        var pages = response.IllustrationPages.GetRange(0, postLimit);

        foreach (var page in pages)
        {
            var url = await DetermineHighestUsableQualityFile(page.IllustrationPagesUrls.All);

            if (url is null)
            {
                continue;
            }

            var file = await GetFile(url);
            
            message.Files.Add(file);
        }

        if (pageCount > postLimit)
        {
            message.Text = $"This is part of a {pageCount} image set.";
        }

        return message;
    }

    private async Task<string?> DetermineHighestUsableQualityFile(IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            var response = await _client.PokeFile(url);

            if (response.Content.Headers.ContentLength < Constants.MaximumFileSize)
            {
                return url;
            }
        }

        return null;
    }

    private async Task<FileAttachment> GetFile(string url)
    {
        var response = await _client.GetFile(url);

        var parsed = new Uri(url);
        
        return new FileAttachment(
            response,
            Path.GetFileName(parsed.AbsolutePath)
        );
    }
}
