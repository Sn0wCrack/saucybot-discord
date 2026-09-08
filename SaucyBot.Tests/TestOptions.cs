using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SaucyBot.Options;
using SaucyBot.Options.Sites;

namespace SaucyBot.Tests;

public static class TestOptions
{
    public static IOptions<BotOptions> BotOptions(this IConfiguration configuration) =>
        Microsoft.Extensions.Options.Options.Create(configuration.GetSection("Bot").Get<BotOptions>() ?? new BotOptions());

    public static IOptions<CacheOptions> CacheOptions(this IConfiguration configuration) =>
        Microsoft.Extensions.Options.Options.Create(configuration.GetSection("Cache").Get<CacheOptions>() ?? new CacheOptions());

    public static IOptions<PixivOptions> PixivOptions(this IConfiguration configuration) =>
        SiteOptions<PixivOptions>(configuration, "Pixiv");

    public static IOptions<FxTwitterOptions> FxTwitterOptions(this IConfiguration configuration) =>
        SiteOptions<FxTwitterOptions>(configuration, "FxTwitter");

    public static IOptions<MisskeyOptions> MisskeyOptions(this IConfiguration configuration) =>
        SiteOptions<MisskeyOptions>(configuration, "Misskey");

    public static IOptions<BlueskyOptions> BlueskyOptions(this IConfiguration configuration) =>
        SiteOptions<BlueskyOptions>(configuration, "Bluesky");

    public static IOptions<ArtStationOptions> ArtStationOptions(this IConfiguration configuration) =>
        SiteOptions<ArtStationOptions>(configuration, "ArtStation");

    public static IOptions<ExHentaiOptions> ExHentaiOptions(this IConfiguration configuration) =>
        SiteOptions<ExHentaiOptions>(configuration, "ExHentai");

    private static IOptions<T> SiteOptions<T>(IConfiguration configuration, string sectionName)
        where T : class, new() =>
        Microsoft.Extensions.Options.Options.Create(configuration.GetSection($"Sites:{sectionName}").Get<T>() ?? new T());
}
