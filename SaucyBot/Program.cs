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

        services.AddSaucyBotTelemetry(configuration);

        var databaseDisabled = configuration.GetSection("Database:Disabled").Get<bool?>() ?? false;

        if (!databaseDisabled)
        {
            services.AddSaucyBotDatabase();
        }

        services.AddSaucyBotCache(configuration);
        var queueOptions = configuration.GetSection("Queue").Get<WorkQueueOptions>() ?? new();
        services.AddSingleton(queueOptions);
        services.AddSingleton<InteractionWorkChannel>();
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(queueOptions.ConnectionString));
        services.AddSingleton<IValkeyStreamClient, StackExchangeValkeyStreamClient>();
        services.AddSingleton<IMessageWorkQueue, ValkeyWorkQueue>();
        services.AddSingleton<IWorkItemProcessor, WorkItemProcessor>();
        services.AddSaucyBotServices(databaseDisabled);
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

        services.AddHostedService<Worker>();
        services.AddHostedService<WorkQueueHostedService>();
    })
    .UseConsoleLifetime()
    .Build()
    .RunAsync();
