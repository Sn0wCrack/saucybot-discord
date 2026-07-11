using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Common;
using SaucyBot.Extensions;
using SaucyBot.Library;
using SaucyBot.Library.Sites.Pixiv;
using SaucyBot.Services;
using SaucyBot.Site.Response;
using Xabe.FFmpeg;

namespace SaucyBot.Site;

public sealed partial class Pixiv : BaseSite
{
    public override string Identifier => "Pixiv";

    [GeneratedRegex(@"https?://(www\.)?pixiv\.net/\S*artworks/(?<id>\d+)/?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex PixivPattern();

    [GeneratedRegex(@"/jump\.php\?(?<url>[^""'\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex JumpUrlPattern();

    protected override Regex Pattern => PixivPattern();
    
    protected override Color Color => new(0x0096fa);

    private readonly IPixivClient _client;
    private readonly ILogger<Pixiv> _logger;
    private readonly IGuildConfigurationManager _guildConfigurationManager;
    private readonly IConfiguration _configuration;

    public Pixiv(
        ILogger<Pixiv> logger,
        IConfiguration configuration,
        IGuildConfigurationManager guildConfigurationManager,
        IPixivClient client
    ) {
        _logger = logger;
        _configuration = configuration;
        _guildConfigurationManager = guildConfigurationManager;
        _client = client;
    }

    public override async Task<ProcessResponse?> Process(ProcessRequest request)
    {
        if (!await _client.Login())
        {
            _logger.LogError("Pixiv login check failed, cookie may be expired or invalid.");
            return null;
        }

        var id = request.Match.Groups["id"].Value;

        var response = await _client.IllustrationDetails(id);

        if (response is null)
        {
            return null;
        }
        
        return response.IllustrationDetails.Type == IllustrationType.Ugoira
            ? await ProcessUgoira(response)
            : await ProcessImage(response, request.Message);
    }

    private async Task<ProcessResponse?> ProcessUgoira(IllustrationDetailsResponse illustrationDetails)
    {
        var response = new ProcessResponse();

        var metadata = await _client.UgoiraMetadata(illustrationDetails.IllustrationDetails.Id);

        if (metadata is null)
        {
            return null;
        }

        using var file = await GetFile(metadata.UgoiraMetadata.OriginalSource);

        var zip = new ZipArchive(file.Stream);

        var basePath = Path.Join(
            Path.GetTempPath(),
            "pixiv",
            $"{illustrationDetails.IllustrationDetails.Id}_{Helper.RandomString()}"
        );

        var concatFile = Path.Join(basePath, "ffconcat");
        
        var format = _configuration.GetSection("Sites:Pixiv:UgoiraFormat").Get<string?>() ?? "mp4";

        var videoFile = Path.Join(basePath, $"ugoira.{format}");
        
        await zip.ExtractToDirectoryAsync(basePath, true);
        
        await File.WriteAllTextAsync(concatFile, BuildConcatFile(metadata.UgoiraMetadata.Frames));

        try
        {
            await RenderUgoiraVideo(concatFile, videoFile);
        }
        catch (Exception ex)
        {
            _logger.LogError("{Message}", ex.Message);
            return null;
        }

        var fileStream = new MemoryStream(
            await File.ReadAllBytesAsync(videoFile)
        );

        var title = illustrationDetails.IllustrationDetails.Title
            .ToLowerInvariant()
            .Replace("-", "")
            .Replace(" ", "_")
            .Trim();

        var fileName = $"{title}_ugoira.{format}";
        
        response.Files.Add(
            new FileAttachment(fileStream, fileName)
        );
        
        Directory.Delete(basePath, true);

        return response;
    }

    private static string BuildConcatFile(List<UgoiraFrame> frames)
    {
        var builder = new StringBuilder("ffconcat version 1.0\n");

        foreach (var (fileName, frameDelay) in frames)
        {
            var duration = Math.Round(frameDelay / 1000.0, 3);
            
            builder
                .Append($"file {fileName}\n")
                .Append($"duration {duration}\n");
        }

        var lastFrame = frames.Last();

        builder.Append($"file {lastFrame.File}\n");

        return builder.ToString();
    }


    private async Task RenderUgoiraVideo(string concatFilePath, string videoFilePath)
    {
        var bitrate = _configuration.GetSection("Sites:Pixiv:UgoiraBitrate").Get<int?>() ?? 2_000;
        
        var conversion = FFmpeg.Conversions.New()
            .SetOverwriteOutput(true)
            .AddParameter("-f concat", ParameterPosition.PreInput)
            .AddParameter($"-i \"{concatFilePath}\"", ParameterPosition.PreInput)
            .AddParameter($"-b:v {bitrate}k")
            .AddParameter("-pix_fmt yuv420p")
            .AddParameter("-filter:v \"pad=ceil(iw/2)*2:ceil(ih/2)*2\"")
            .SetOutput(videoFilePath);

        await conversion.Start();
    }

    private async Task<ProcessResponse?> ProcessImage(IllustrationDetailsResponse illustrationDetails, SocketUserMessage? message)
    {
        var response = new ProcessResponse();

        var pageCount = illustrationDetails.IllustrationDetails.PageCount;
        
        var postLimit =  _configuration.GetSection("Sites:Pixiv:PostLimit").Get<int>();

        if (message is not null)
        {
            var guildConfiguration = await _guildConfigurationManager.GetByChannel(message.Channel);

            if (guildConfiguration is not null)
            {
                postLimit = (int) guildConfiguration.MaximumPixivImages;
            }
        }

        if (pageCount == 1)
        {
            var file = await DetermineHighestUsableQualityFile(
                illustrationDetails.IllustrationDetails.IllustrationDetailsUrls.AllWithoutThumbnails
            );

            if (file is not null)
            {
                response.Files.Add(file.Value);
            }
        }
        else
        {
            var illustrationPagesResponse = await _client.IllustrationPages(illustrationDetails.IllustrationDetails.Id);

            if (illustrationPagesResponse is null)
            {
                return response;
            }

            var pages = illustrationPagesResponse.IllustrationPages.SafeSlice(0, postLimit);

            var fileTasks = pages.Select(page => DetermineHighestUsableQualityFile(page.IllustrationPagesUrls.AllWithoutOriginalAndThumbnails));

            var files = await Task.WhenAll(fileTasks);

            foreach (var file in files)
            {
                if (file is not null)
                {
                    response.Files.Add(file.Value);
                }
            }
        }

        var componentBuilder = new ComponentBuilderV2();

        var container = new ContainerBuilder
        {
            AccentColor = this.Color
        };
        
        container.AddComponent(
            new TextDisplayBuilder().WithContent($"## [{illustrationDetails.IllustrationDetails.Title}]({illustrationDetails.IllustrationDetails.Url})")
        );

        if (illustrationDetails.IllustrationDetails.Description is not "")
        {
            container.AddComponent(
                new TextDisplayBuilder().WithContent(Helper.HtmlToMarkdown(CleanPixivHtml(illustrationDetails.IllustrationDetails.Description)))
            );
        }
        
        var mediaGallery = new MediaGalleryBuilder();

        foreach (var file in response.Files)
        {
            mediaGallery.AddItem($"attachment://{file.FileName}");
        }

        container.AddComponent(mediaGallery);
        
        if (pageCount > postLimit)
        {
            container.AddComponent(
                new TextDisplayBuilder().WithContent($"This is part of a {pageCount} image set.")
            );
        }

        componentBuilder.AddComponent(container);
        
        response.Components = componentBuilder.Build();

        return response;
    }

    private async Task<FileAttachment?> DetermineHighestUsableQualityFile(IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            _logger.LogDebug("Attempting to download {Url}...", url);

            var stream = await _client.GetFile(url);

            if (stream.Length < Constants.MaximumFileSize)
            {
                var parsed = new Uri(url);

                return new FileAttachment(stream, Path.GetFileName(parsed.AbsolutePath));
            }

            await stream.DisposeAsync();
        }

        return null;
    }

    private async Task<FileAttachment> GetFile(string url)
    {
        _logger.LogDebug("Attempting to download {Url}...", url);
        
        var response = await _client.GetFile(url);

        var parsed = new Uri(url);
        
        return new FileAttachment(
            response,
            Path.GetFileName(parsed.AbsolutePath)
        );
    }

    private static string CleanPixivHtml(string html)
    {
        return JumpUrlPattern().Replace(html, match =>
        {
            var encoded = match.Groups["url"].Value;
            return WebUtility.UrlDecode(encoded);
        });
    }
}
