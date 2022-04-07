using SaucyBot;
using SaucyBot.Database;
using SaucyBot.Library.Sites.ArtStation;
using SaucyBot.Library.Sites.FurAffinity;
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
        services.AddSingleton<CacheManager>();

        services.AddSingleton<MemoryCacheDriver>();
        services.AddSingleton<RedisCacheDriver>();

        services.AddSingleton<GuildConfigurationManager>();
        
        services.AddSingleton<FaExportClient>();
        services.AddSingleton<PixivClient>();
        services.AddSingleton<ArtStationClient>();

        services.AddSingleton<FurAffinity>();
        services.AddSingleton<Pixiv>();
        services.AddSingleton<ArtStation>();

        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();
