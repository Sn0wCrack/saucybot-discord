using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Microsoft.Extensions.Options;
using SaucyBot.Common;
using SaucyBot.Database.Models;
using SaucyBot.Extensions;
using SaucyBot.Extensions.Discord;
using SaucyBot.Library;
using SaucyBot.Library.Sites.Pixiv;
using SaucyBot.Options.Sites;

namespace SaucyBot.Site.Pixiv;


[SiteIdentifier("Pixiv")]
public sealed partial class PixivSite : BaseSite
{
    [GeneratedRegex(@"https?://(www\.)?pixiv\.net/\S*artworks/(?<id>\d+)/?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex PixivPattern();

    [GeneratedRegex(@"/jump\.php\?(?<url>[^""'\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex JumpUrlPattern();

    public override Regex Pattern => PixivPattern();

    public override Color Color => new(0x0096fa);

    private readonly IPixivClient _client;
    private readonly ILogger<PixivSite> _logger;
    private readonly PixivOptions _pixivOptions;
    private readonly IUgoiraVideoRenderer _ugoiraVideoRenderer;

    public PixivSite(
        ILogger<PixivSite> logger,
        IOptions<PixivOptions> pixivOptions,
        IPixivClient client,
        IUgoiraVideoRenderer ugoiraVideoRenderer
    )
    {
        _logger = logger;
        _pixivOptions = pixivOptions.Value;
        _client = client;
        _ugoiraVideoRenderer = ugoiraVideoRenderer;
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

        var cancellationToken = request.Context?.CancellationToken ?? default;

        return response.IllustrationDetails.Type == IllustrationType.Ugoira
            ? await ProcessUgoira(response.IllustrationDetails, cancellationToken)
            : await ProcessImage(response.IllustrationDetails, request.GuildConfiguration, cancellationToken);
    }

    private async Task<ProcessResponse?> ProcessUgoira(IllustrationDetails details, CancellationToken cancellationToken)
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

        using var file = await GetFile(metadata.UgoiraMetadata.OriginalSource, cancellationToken);

        using var zip = new ZipArchive(file.Stream);

        var basePath = Path.Join(
            Path.GetTempPath(),
            "pixiv",
            $"{details.Id}_{Helper.RandomString()}"
        );

        var concatFile = Path.Join(basePath, "ffconcat");

        var codec = _pixivOptions.Ugoira.Codec ?? UgoiraCodec.H264;

        var fileExtension = codec switch
        {
            UgoiraCodec.H264 or UgoiraCodec.AV1 => "mp4",
            UgoiraCodec.VP9 => "webm",
            _ => "mp4"
        };

        var videoFile = Path.Join(basePath, $"ugoira.{fileExtension}");

        FileStream? fileStream = null;
        var cleanupAttempted = false;

        try
        {
            await zip.ExtractToDirectoryAsync(basePath, true, cancellationToken);
            await File.WriteAllTextAsync(concatFile, BuildConcatFile(metadata.UgoiraMetadata.Frames), cancellationToken);

            try
            {
                await _ugoiraVideoRenderer.RenderAsync(concatFile, videoFile, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError("{Message}", ex.Message);
                cleanupAttempted = true;
                try
                {
                    Directory.Delete(basePath, true);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(cleanupException, "Failed to clean up rendered Pixiv media at {Path}", basePath);
                }
                throw;
            }

            fileStream = new FileStream(videoFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

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
            var result = new ProcessResponse(
                files: response.Files,
                components: componentBuilder.Build(),
                nsfw: response.IsNsfw
            );

            cleanupAttempted = true;
            Directory.Delete(basePath, true);
            fileStream = null;
            return result;
        }
        catch
        {
            if (fileStream is not null)
            {
                try
                {
                    await fileStream.DisposeAsync();
                }
                catch
                {
                    // Preserve the failure that caused attachment construction or cleanup to fail.
                }
            }

            if (!cleanupAttempted && Directory.Exists(basePath))
            {
                try
                {
                    cleanupAttempted = true;
                    Directory.Delete(basePath, true);
                }
                catch
                {
                    // Preserve the original processing failure.
                }
            }

            throw;
        }
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


    private async Task<ProcessResponse?> ProcessImage(
        IllustrationDetails details,
        GuildConfiguration? guildConfiguration,
        CancellationToken cancellationToken)
    {
        var response = new ProcessResponse
        {
            IsNsfw = details.IsNsfw,
        };

        try
        {
            var pageCount = details.PageCount;

            var postLimit = (int?)guildConfiguration?.MaximumPixivImages ?? _pixivOptions.PostLimit;

            if (pageCount == 1)
            {
                var file = await DetermineHighestUsableQualityFile(
                    details.IllustrationDetailsUrls.AllWithoutThumbnails,
                    cancellationToken
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

                var fileTasks = pages
                    .Select(page => DetermineHighestUsableQualityFile(page.IllustrationPagesUrls.AllWithoutOriginalAndThumbnails, cancellationToken))
                    .ToArray();

                FileAttachment?[] files;
                try
                {
                    files = await Task.WhenAll(fileTasks);
                }
                catch
                {
                    foreach (var task in fileTasks.Where(task => task.IsCompletedSuccessfully))
                    {
                        var file = task.Result;
                        if (file is not null)
                        {
                            await file.Value.Stream.DisposeAsync();
                        }
                    }

                    throw;
                }

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

            return new ProcessResponse(
                files: response.Files,
                components: componentBuilder.Build(),
                nsfw: response.IsNsfw
            );
        }
        catch
        {
            await response.DisposeAsync();
            throw;
        }
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
            .WithTextDisplay($"### [{Helper.RemoveEmojis(details.Title)}]({details.Url})")
            .WithTextDisplay($"👤 [{details.UserName}]({details.UserUrl})")
            .WithSeparator()
            .When(details.Description is not "", (builder => builder.WithTextDisplay(Helper.HtmlToMarkdown(CleanPixivHtml(details.Description)))))
            .WithMediaGallery(files.Select(x => $"attachment://{x.FileName}"))
            .WithSeparator()
            .WithTextDisplay(string.Join("    ", stats))
            .WithTextDisplay($"-# Posted: <t:{details.CreateDate.ToUnixTimeSeconds()}:F>");
    }

    private async Task<FileAttachment?> DetermineHighestUsableQualityFile(IEnumerable<string> urls, CancellationToken cancellationToken)
    {
        foreach (var url in urls)
        {
            _logger.LogDebug("Attempting to download {Url}...", url);

            var stream = await _client.GetFile(url, cancellationToken);

            try
            {
                if (stream.Length < Constants.MaximumFileSize)
                {
                    var parsed = new Uri(url);

                    return new FileAttachment(stream, Path.GetFileName(parsed.AbsolutePath));
                }
            }
            catch
            {
                await stream.DisposeAsync();
                throw;
            }

            await stream.DisposeAsync();
        }

        return null;
    }

    private async Task<FileAttachment> GetFile(string url, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Attempting to download {Url}...", url);

        var response = await _client.GetFile(url, cancellationToken);

        try
        {
            var parsed = new Uri(url);

            return new FileAttachment(
                response,
                Path.GetFileName(parsed.AbsolutePath)
            );
        }
        catch
        {
            await response.DisposeAsync();
            throw;
        }
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
