using SaucyBot;
using SaucyBot.Database;
using SaucyBot.Library.Sites.ArtStation;
using SaucyBot.Library.Sites.DeviantArt;
using SaucyBot.Library.Sites.E621;
using SaucyBot.Library.Sites.ExHentai;
using SaucyBot.Library.Sites.FurAffinity;
using SaucyBot.Library.Sites.HentaiFoundry;
using SaucyBot.Library.Sites.Newgrounds;
using SaucyBot.Library.Sites.Pixiv;
using SaucyBot.Services;
using SaucyBot.Services.Cache;
using SaucyBot.Site;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((context, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console();
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        
        services.AddDbContext<DatabaseContext>(ServiceLifetime.Transient);
        services.AddDbContextFactory<DatabaseContext>(lifetime: ServiceLifetime.Transient);

        services.AddMemoryCache();
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetSection("Cache:Redis:ConnectionString").Get<string>();
        });
        
        services.AddSingleton<SiteManager>();
        services.AddSingleton<MessageManager>();
        services.AddSingleton<DatabaseManager>();
        services.AddSingleton<ICacheManager, CacheManager>();

        services.AddSingleton<MemoryCacheDriver>();
        services.AddSingleton<RedisCacheDriver>();

        services.AddSingleton<IGuildConfigurationManager, GuildConfigurationManager>();
        
        services.AddSingleton<IFurAffinityClient, FaExportClient>();
        services.AddSingleton<IPixivClient, PixivClient>();
        services.AddSingleton<IArtStationClient, ArtStationClient>();
        services.AddSingleton<HentaiFoundryClient>();
        services.AddSingleton<NewgroundsClient>();
        services.AddSingleton<ExHentaiClient>();
        services.AddSingleton<DeviantArtOpenEmbedClient>();
        services.AddSingleton<DeviantArtClient>();
        services.AddSingleton<E621Client>();

        services.AddSingleton<FurAffinity>();
        services.AddSingleton<Pixiv>();
        services.AddSingleton<ArtStation>();
        services.AddSingleton<HentaiFoundry>();
        services.AddSingleton<Twitter>();
        services.AddSingleton<DeviantArt>();
        services.AddSingleton<E621>();
        services.AddSingleton<ExHentai>();
        services.AddSingleton<Newgrounds>();

        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();
