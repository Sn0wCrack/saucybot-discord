using SaucyBot.Site.ArtStation;
using SaucyBot.Site.Bluesky;
using SaucyBot.Site.DeviantArt;
using SaucyBot.Site.E621;
using SaucyBot.Site.ExHentai;
using SaucyBot.Site.FurAffinity;
using SaucyBot.Site.HentaiFoundry;
using SaucyBot.Site.Instagram;
using SaucyBot.Site.Misskey;
using SaucyBot.Site.Newgrounds;
using SaucyBot.Site.Pixiv;
using SaucyBot.Site.Reddit;
using SaucyBot.Site.Twitter;

namespace SaucyBot.Site;

public static class SiteServiceRegistration
{
    public static IServiceCollection AddSaucyBotSites(this IServiceCollection services)
    {
        services.AddSingleton<IArtStationSite, ArtStation.ArtStation>();
        services.AddSingleton<IBlueskySite, Bluesky.Bluesky>();
        services.AddSingleton<IDeviantArtSite, DeviantArt.DeviantArt>();
        services.AddSingleton<IE621Site, E621.E621>();
        services.AddSingleton<IExHentaiSite, ExHentai.ExHentai>();
        services.AddSingleton<IFurAffinitySite, FurAffinity.XFurAffinity>();
        services.AddSingleton<IHentaiFoundrySite, HentaiFoundry.HentaiFoundry>();
        services.AddSingleton<IInstagramSite, Instagram.Instagram>();
        services.AddSingleton<IMisskeySite, Misskey.Misskey>();
        services.AddSingleton<INewgroundsSite, Newgrounds.Newgrounds>();
        services.AddSingleton<IPixivSite, Pixiv.Pixiv>();
        services.AddSingleton<IRedditSite, Reddit.Reddit>();
        services.AddSingleton<ITwitterSite, Twitter.FxTwitter>();
        return services;
    }
}
