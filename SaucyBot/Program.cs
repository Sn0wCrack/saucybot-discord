using Microsoft.Extensions.Options;
using SaucyBot;
using SaucyBot.Database;
using SaucyBot.Diagnostics;
using SaucyBot.Library.Sites;
using SaucyBot.Library.Sites.ArtStation;
using SaucyBot.Library.Sites.BlueSky;
using SaucyBot.Library.Sites.DeviantArt;
using SaucyBot.Library.Sites.E621;
using SaucyBot.Library.Sites.ExHentai;
using SaucyBot.Library.Sites.FurAffinity;
using SaucyBot.Library.Sites.HentaiFoundry;
using SaucyBot.Library.Sites.Misskey;
using SaucyBot.Library.Sites.Newgrounds;
using SaucyBot.Library.Sites.Pixiv;
using SaucyBot.Library.Sites.Twitter;
using SaucyBot.Options;
using SaucyBot.Options.Sites;
using SaucyBot.Queue;
using SaucyBot.Services;
using SaucyBot.Services.Cache;
using SaucyBot.Site;
using Serilog;
using StackExchange.Redis;

await Host.CreateDefaultBuilder(args)
    .UseSerilog((context, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        services.Configure<BotOptions>(configuration.GetSection("Bot"));
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));
        services.Configure<CacheOptions>(configuration.GetSection("Cache"));
        services.Configure<SentryOptions>(configuration.GetSection("Sentry"));
        services.Configure<TelemetryOptions>(configuration.GetSection("OpenTelemetry"));
        services.Configure<PixivOptions>(configuration.GetSection("Sites:Pixiv"));
        services.Configure<FxTwitterOptions>(configuration.GetSection("Sites:FxTwitter"));
        services.Configure<MisskeyOptions>(configuration.GetSection("Sites:Misskey"));
        services.Configure<BlueskyOptions>(configuration.GetSection("Sites:Bluesky"));
        services.Configure<ArtStationOptions>(configuration.GetSection("Sites:ArtStation"));
        services.Configure<ExHentaiOptions>(configuration.GetSection("Sites:ExHentai"));
        services.Configure<HentaiFoundryOptions>(configuration.GetSection("Sites:HentaiFoundry"));
        services.Configure<FurAffinityOptions>(configuration.GetSection("Sites:FurAffinity"));

        services.AddSaucyBotTelemetry(
            Options.Create(configuration.GetSection("OpenTelemetry").Get<TelemetryOptions>() ?? new TelemetryOptions()));
        services.AddSaucyBotSentry(
            Options.Create(configuration.GetSection("Sentry").Get<SentryOptions>() ?? new SentryOptions()));

        services.AddSaucyBotDatabase();

        services.AddSaucyBotCache(configuration);
        var queueOptions = configuration.GetSection("Queue").Get<WorkQueueOptions>() ?? new();
        services.AddSingleton(queueOptions);
        services.AddSingleton<InteractionWorkChannel>();
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(queueOptions.ConnectionString));
        services.AddSingleton<IRedisStreamClient, StackExchangeRedisStreamClient>();
        services.AddSingleton<IMessageWorkQueue, RedisWorkQueue>();
        services.AddSingleton<IWorkItemProcessor, WorkItemProcessor>();
        services.AddSaucyBotServices();
        services.AddSaucyBotSites();

        services.AddFurAffinityClient(configuration);
        services.AddArtStationClient();
        services.AddNewgroundsClient();
        services.AddDeviantArtOpenEmbedClient();
        services.AddE621Client();
        services.AddFxTwitterClient();
        services.AddTwitterImageSyndicationClient();
        services.AddMisskeyClient();
        services.AddVixBlueskyClient();
        services.AddPixivClient(configuration);
        services.AddExHentaiClient(configuration);
        services.AddHentaiFoundryClient(configuration);
        services.AddDeviantArtClient();
        services.AddFileDownloadClient();

        services.AddSingleton<WorkQueueHostedService>();
        services.AddHostedService(provider => provider.GetRequiredService<WorkQueueHostedService>());
        services.AddSingleton<DiscordClientHost>();
        services.AddHostedService<Worker>();
    })
    .UseConsoleLifetime()
    .Build()
    .RunAsync();
