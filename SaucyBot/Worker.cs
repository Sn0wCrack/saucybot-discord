using Discord;
using Discord.WebSocket;
using SaucyBot.Library;
using SaucyBot.Services;

namespace SaucyBot;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;

    private readonly DatabaseManager _databaseManager;
    private readonly SiteManager _siteManager;

    private DiscordShardedClient? _client;

    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        DatabaseManager databaseManager,
        SiteManager siteManager
    ) {
        _logger = logger;
        _configuration = configuration;
        _databaseManager = databaseManager;
        _siteManager = siteManager;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _databaseManager.EnsureAllMigrationsHaveRun();

        var config = new DiscordSocketConfig()
        {
            GatewayIntents = Constants.RequiredGatewayIntents,
            MessageCacheSize = _configuration.GetSection("Bot:MessageCacheSize").Get<int?>() ?? 100,
            ConnectionTimeout = _configuration.GetSection("Bot:ConnectionTimeout").Get<int?>() ?? 30000,
            AlwaysDownloadUsers = false,
            AlwaysResolveStickers = false,
            AlwaysDownloadDefaultStickers = false,
        };

        _client = new DiscordShardedClient(config);
        
        _client.Log += HandleLogAsync;
        _client.ShardReady += HandleShardReadyAsync;
        _client.MessageReceived += HandleMessageAsync;

        await _client.LoginAsync(TokenType.Bot, _configuration.GetSection("Bot:DiscordToken").Get<string>());
        await _client.StartAsync();
    }

    protected override Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            await _client.StopAsync();
            await _client.DisposeAsync();
        }
    }

    private Task HandleMessageAsync(SocketMessage socketMessage)
    {
        if (socketMessage is not SocketUserMessage message)
        {
            return Task.CompletedTask;
        }

        // Ignore Messages created by the Bot itself
        if (socketMessage.Author.Id == _client?.CurrentUser.Id)
        {
            return Task.CompletedTask;
        }

        Task.Run(async () => await _siteManager.Handle(message));

        return Task.CompletedTask;
    }

    private Task HandleShardReadyAsync(DiscordSocketClient client)
    {
        _logger.LogInformation("[{Source}] {Message}", $"Shard #{client.ShardId}", "Ready");

        return Task.CompletedTask;
    }

    private Task HandleLogAsync(LogMessage message)
    {

        var severity = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Trace,
            LogSeverity.Debug => LogLevel.Debug,
            _ => LogLevel.Information
        };
        
        _logger.Log(severity, message.Exception, "[{Source}] {Message}", message.Source, message.Message);

        return Task.CompletedTask;
    }
}
