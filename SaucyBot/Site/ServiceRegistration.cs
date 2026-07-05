using Microsoft.Extensions.DependencyInjection;

namespace SaucyBot.Site;

public static class SiteServiceRegistration
{
    public static IServiceCollection AddSaucyBotSites(this IServiceCollection services)
    {
        services.AddSingleton<FurAffinity>();
        services.AddSingleton<Pixiv>();
        services.AddSingleton<ArtStation>();
        services.AddSingleton<HentaiFoundry>();
        services.AddSingleton<FxTwitter>();
        services.AddSingleton<DeviantArt>();
        services.AddSingleton<E621>();
        services.AddSingleton<ExHentai>();
        services.AddSingleton<Newgrounds>();
        services.AddSingleton<Reddit>();
        services.AddSingleton<Misskey>();
        services.AddSingleton<Bluesky>();
        services.AddSingleton<Instagram>();
        return services;
    }
}
