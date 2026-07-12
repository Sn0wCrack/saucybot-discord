using Microsoft.Extensions.DependencyInjection;

namespace SaucyBot.Site;

public static class SiteServiceRegistration
{
    public static IServiceCollection AddSaucyBotSites(this IServiceCollection services)
    {
        services.AddSingleton<IArtStationSite, ArtStation>();
        services.AddSingleton<IBlueskySite, Bluesky>();
        services.AddSingleton<IDeviantArtSite, DeviantArt>();
        services.AddSingleton<IE621Site, E621>();
        services.AddSingleton<IExHentaiSite, ExHentai>();
        services.AddSingleton<IFurAffinitySite, FurAffinity>();
        services.AddSingleton<IHentaiFoundrySite, HentaiFoundry>();
        services.AddSingleton<IInstagramSite, Instagram>();
        services.AddSingleton<IMisskeySite, Misskey>();
        services.AddSingleton<INewgroundsSite, Newgrounds>();
        services.AddSingleton<IPixivSite, Pixiv>();
        services.AddSingleton<IRedditSite, Reddit>();
        services.AddSingleton<ITwitterSite, FxTwitter>();
        services.AddSingleton<IXFuraffinitySite, XFurAffinity>();
        return services;
    }
}
