using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Discord;
using SaucyBot.Common;
using SaucyBot.Database.Models;
using SaucyBot.Extensions;
using SaucyBot.Extensions.Discord;
using SaucyBot.Library;
using SaucyBot.Library.Sites.Pixiv;
using Xabe.FFmpeg;

namespace SaucyBot.Site.Pixiv;


public sealed partial class PixivSite : BaseSite, IPixivSite
{
    public override string Identifier => "Pixiv";

    [GeneratedRegex(@"https?://(www\.)?pixiv\.net/\S*artworks/(?<id>\d+)/?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex PixivPattern();

    [GeneratedRegex(@"/jump\.php\?(?<url>[^""'\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex JumpUrlPattern();

    public override Regex Pattern => PixivPattern();

    public override Color Color => new(0x0096fa);

    private readonly IPixivClient _client;
    private readonly ILogger<PixivSite> _logger;
    private readonly IConfiguration _configuration;

    public PixivSite(
        ILogger<PixivSite> logger,
        IConfiguration configuration,
        IPixivClient client
    )
    {
        _logger = logger;
        _configuration = configuration;
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
            ? await ProcessUgoira(response.IllustrationDetails)
            : await ProcessImage(response.IllustrationDetails, request.GuildConfiguration);
    }

    private async Task<ProcessResponse?> ProcessUgoira(IllustrationDetails details)
    {
        var response = new ProcessResponse
        {
            IsNsfw = details.IsNsfw,
        };

        var metadata = await _client.UgoiraMetadata(details.Id);

        if (metadata is null)
        {
            return null;
        }

        using var file = await GetFile(metadata.UgoiraMetadata.OriginalSource);

        var zip = new ZipArchive(file.Stream);

        var basePath = Path.Join(
            Path.GetTempPath(),
            "pixiv",
            $"{details.Id}_{Helper.RandomString()}"
        );

        var concatFile = Path.Join(basePath, "ffconcat");

        var codec = _configuration.GetSection("Sites:Pixiv:Ugoira:Codec").Get<UgoiraCodec?>() ?? UgoiraCodec.H264;

        var fileExtension = codec switch
        {
            UgoiraCodec.H264 or UgoiraCodec.AV1 => "mp4",
            UgoiraCodec.VP9 => "webm",
            _ => "mp4"
        };

        var videoFile = Path.Join(basePath, $"ugoira.{fileExtension}");

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

        var title = details.Title
            .ToLowerInvariant()
            .Replace("-", "")
            .Replace(" ", "_")
            .Trim();

        var fileName = $"{title}_ugoira.{fileExtension}";

        response.Files.Add(
            new FileAttachment(fileStream, fileName)
        );

        var componentBuilder = new ComponentBuilderV2()
            .WithContainer(
                BuildContainerComponent(details, response.Files)
            );
        response.Components = componentBuilder.Build();

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
        var conversion = FFmpeg.Conversions.New()
            .SetOverwriteOutput(true)
            .AddParameter("-f concat", ParameterPosition.PreInput)
            .AddParameter($"-i \"{concatFilePath}\"", ParameterPosition.PreInput)
            .AddParameter("-pix_fmt yuv420p")
            .AddParameter("-filter:v \"pad=ceil(iw/2)*2:ceil(ih/2)*2\"")
            .SetOutput(videoFilePath);

        var codec = _configuration.GetSection("Sites:Pixiv:Ugoira:Codec").Get<UgoiraCodec?>() ?? UgoiraCodec.H264;
        var bitrate = _configuration.GetSection("Sites:Pixiv:Ugoira:Bitrate").Get<int?>() ?? 2_000;

        switch (codec)
        {
            default:
            case UgoiraCodec.H264:
                conversion
                    .AddParameter("-c:v libx264")
                    .AddParameter($"-b:v {bitrate}k");
                break;
            case UgoiraCodec.AV1:
                var preset = _configuration.GetSection("Sites:Pixiv:Ugoira:Preset").Get<int?>() ?? 6;
                var crf = _configuration.GetSection("Sites:Pixiv:Ugoira:CRF").Get<int?>() ?? 40;

                conversion
                    .AddParameter("-c:v libsvtav1")
                    .AddParameter($"-preset {preset}")
                    .AddParameter($"-crf {crf}")
                    .AddParameter($"-maxrate {bitrate}k")
                    .AddParameter($"-bufsize {bitrate * 2}k");
                break;
            case UgoiraCodec.VP9:
                conversion
                    .AddParameter("-c:v libvp9")
                    .AddParameter($"-b:v {bitrate}k");

                break;
        }

        await conversion.Start();
    }

    private async Task<ProcessResponse?> ProcessImage(IllustrationDetails details, GuildConfiguration? guildConfiguration)
    {
        var response = new ProcessResponse
        {
            IsNsfw = details.IsNsfw,
        };

        var pageCount = details.PageCount;

        var postLimit = (int?)guildConfiguration?.MaximumPixivImages ?? _configuration.GetSection("Sites:Pixiv:PostLimit").Get<int>();

        if (pageCount == 1)
        {
            var file = await DetermineHighestUsableQualityFile(
                details.IllustrationDetailsUrls.AllWithoutThumbnails
            );

            if (file is not null)
            {
                response.Files.Add(file.Value);
            }
        }
        else
        {
            var illustrationPagesResponse = await _client.IllustrationPages(details.Id);

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

        var componentBuilder = new ComponentBuilderV2()
            .WithContainer(
                BuildContainerComponent(details, response.Files)
                    .When(pageCount > postLimit, (builder => builder.WithTextDisplay($"-## This is part of a {pageCount} image set.")))
             );

        response.Components = componentBuilder.Build();

        return response;
    }

    private ContainerBuilder BuildContainerComponent(IllustrationDetails details, IEnumerable<FileAttachment> files)
    {
        var stats = new List<string>();

        if (details.IsAi)
        {
            stats.Add("🤖 AI Generated");
        }

        stats.AddRange([
            $"{details.Likes:N0} 🙂",
            $"{details.Bookmarks:N0} ❤️",
            $"{details.Views:N0} 👀",
        ]);

        return new ContainerBuilder()
            .WithAccentColor(this.Color)
            .WithTextDisplay($"### [{details.Title}]({details.Url})")
            .WithTextDisplay($"👤 [{details.UserName}]({details.UserUrl})")
            .WithSeparator()
            .When(details.Description is not "", (builder => builder.WithTextDisplay(Helper.HtmlToMarkdown(CleanPixivHtml(details.Description)))))
            .WithMediaGallery(files.Select(x => $"attachment://{x.FileName}"))
            .WithSeparator()
            .WithTextDisplay(string.Join("    ", stats))
            .WithTextDisplay($"-# Posted: <t:{details.CreateDate.ToUnixTimeSeconds()}:F>");
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
